using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// On startup, pulls the LightRAG image once and ensures every agent in the DB has a
/// running dedicated container, back-filling/repairing <c>Agent.LightRagPort</c>.
/// Runs as a background service so it does not block app startup (the first-run image
/// pull can take minutes); <see cref="ILightRagContainerManager.EnsureContainerAsync"/>
/// also self-pulls, so agent creation works even before this finishes.
/// </summary>
public sealed class LightRagReconcilerHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILightRagContainerManager _containerManager;
    private readonly ILogger<LightRagReconcilerHostedService> _logger;

    public LightRagReconcilerHostedService(
        IServiceProvider serviceProvider,
        ILightRagContainerManager containerManager,
        ILogger<LightRagReconcilerHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _containerManager = containerManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _containerManager.EnsureImagePulledAsync(stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var agents = await db.Agents.ToListAsync(stoppingToken);

            foreach (var agent in agents)
            {
                try
                {
                    var port = await _containerManager.EnsureContainerAsync(agent.Id, agent.LightRagPort, stoppingToken);
                    if (agent.LightRagPort != port)
                    {
                        agent.LightRagPort = port;
                        _logger.LogInformation("LightRAG reconciler: agent {AgentId} port set to {Port}.", agent.Id, port);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LightRAG reconciler: failed to ensure container for agent {AgentId}.", agent.Id);
                }
            }

            await db.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("LightRAG reconciler: reconciled {Count} agent container(s).", agents.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App is shutting down — nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LightRAG reconciler: startup reconciliation failed.");
        }
    }
}
