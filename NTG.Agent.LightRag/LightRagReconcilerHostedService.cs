using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NTG.Agent.LightRag;

/// <summary>
/// On startup, pulls the LightRAG image once and ensures every agent has a running
/// dedicated container, back-filling/repairing its port reservation.
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
            var portStore = scope.ServiceProvider.GetRequiredService<ILightRagAgentPortStore>();
            var provisioner = scope.ServiceProvider.GetRequiredService<ILightRagProvisioner>();
            var agentIds = await portStore.GetAgentIdsAsync(stoppingToken);

            foreach (var agentId in agentIds)
            {
                try
                {
                    // Reserve the agent's identity-bound port and ensure its container runs
                    // on it (reassign + retry once on external port conflict).
                    await provisioner.ProvisionAsync(agentId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LightRAG reconciler: failed to ensure container for agent {AgentId}.", agentId);
                }
            }

            _logger.LogInformation("LightRAG reconciler: reconciled {Count} agent container(s).", agentIds.Count);
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
