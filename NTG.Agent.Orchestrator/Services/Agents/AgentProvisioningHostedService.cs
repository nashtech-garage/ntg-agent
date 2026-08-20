using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Knowledge;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Drives newly-created (and retried) agents through knowledge-backend provisioning without blocking
/// the create request. For every agent still in <see cref="AgentProvisioningStatus.Provisioning"/> it
/// calls <see cref="IKnowledgeProvisioner.ProvisionAgentAsync"/> and flips the row to
/// <see cref="AgentProvisioningStatus.Ready"/> or <see cref="AgentProvisioningStatus.Failed"/> (with the
/// error message), so the Admin UI can reflect the true state and developers can trace transitions in logs.
///
/// The worker is <b>event-driven</b>: it only works while agents are provisioning. When none remain it
/// parks on <see cref="AgentProvisioningSignal.WaitAsync"/> (no timer, no DB queries) until a create /
/// reprovision calls <see cref="AgentProvisioningSignal.Notify"/>. A drain pass also runs once at startup
/// to recover any agents left Provisioning by a previous run. Mirrors <c>LightRagIngestionStatusHostedService</c>.
/// </summary>
public sealed class AgentProvisioningHostedService : BackgroundService
{
    // Container boot dominates each provision, so a short delay between passes is plenty and keeps
    // one failing agent from hot-looping.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceProvider _serviceProvider;
    private readonly AgentProvisioningSignal _signal;
    private readonly ILogger<AgentProvisioningHostedService> _logger;

    public AgentProvisioningHostedService(
        IServiceProvider serviceProvider,
        AgentProvisioningSignal signal,
        ILogger<AgentProvisioningHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain: keep provisioning while any agent is still Provisioning. The first iteration
                // also recovers agents left mid-provision by a previous run.
                while (await ProvisionPendingAsync(stoppingToken))
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent provisioning pass failed.");
                await Task.Delay(PollInterval, stoppingToken);
            }

            // Idle: nothing to do — park until a create/reprovision signals new work.
            try
            {
                await _signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Provisions each agent currently in Provisioning state once; returns true if any remained
    /// Provisioning (i.e. was newly picked up this pass) so the caller keeps draining.
    /// </summary>
    private async Task<bool> ProvisionPendingAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var provisioner = scope.ServiceProvider.GetRequiredService<IKnowledgeProvisioner>();

        var pending = await db.Agents
            .Where(a => a.ProvisioningStatus == AgentProvisioningStatus.Provisioning)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (pending.Count == 0) return false;

        foreach (var agentId in pending)
        {
            ct.ThrowIfCancellationRequested();

            // Each agent gets its own scope so a slow/failing provision doesn't hold a stale
            // change-tracker, and one agent's failure never rolls back another's status write.
            using var agentScope = _serviceProvider.CreateScope();
            var agentDb = agentScope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var agentProvisioner = agentScope.ServiceProvider.GetRequiredService<IKnowledgeProvisioner>();

            var agent = await agentDb.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
            if (agent is null) continue; // deleted while queued — nothing to do.

            try
            {
                await agentProvisioner.ProvisionAgentAsync(agentId, ct);
                agent.ProvisioningStatus = AgentProvisioningStatus.Ready;
                agent.ProvisioningError = null;
                agent.ProvisionedAt = DateTime.UtcNow;
                _logger.LogInformation("Agent provisioning succeeded: agentId={AgentId}", agentId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                agent.ProvisioningStatus = AgentProvisioningStatus.Failed;
                agent.ProvisioningError = ex.Message;
                agent.ProvisionedAt = DateTime.UtcNow;
                _logger.LogError(ex, "Agent provisioning failed: agentId={AgentId} reason={Reason}", agentId, ex.Message);
            }

            await agentDb.SaveChangesAsync(ct);
        }

        return true;
    }
}
