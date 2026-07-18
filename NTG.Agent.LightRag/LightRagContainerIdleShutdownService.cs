using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Background service that periodically checks whether LightRAG containers have been
/// idle beyond the configured timeout and stops them to reclaim RAM. Stopped containers
/// are automatically restarted on the next request via
/// <see cref="LightRagClientFactory.GetClientAsync"/>.
/// </summary>
public sealed class LightRagContainerIdleShutdownService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILightRagContainerManager _containerManager;
    private readonly LightRagContainerAccessTracker _accessTracker;
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagContainerIdleShutdownService> _logger;

    public LightRagContainerIdleShutdownService(
        IServiceProvider serviceProvider,
        ILightRagContainerManager containerManager,
        LightRagContainerAccessTracker accessTracker,
        IOptions<LightRagSettings> settings,
        ILogger<LightRagContainerIdleShutdownService> logger)
    {
        _serviceProvider = serviceProvider;
        _containerManager = containerManager;
        _accessTracker = accessTracker;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Idle shutdown is disabled when timeout is zero or negative.
        if (_settings.IdleTimeoutMinutes <= 0)
        {
            _logger.LogInformation(
                "LightRAG idle shutdown is disabled (IdleTimeoutMinutes={Timeout}).",
                _settings.IdleTimeoutMinutes);
            return;
        }

        var interval = TimeSpan.FromMinutes(_settings.IdleCheckIntervalMinutes);
        _logger.LogInformation(
            "LightRAG idle shutdown service started: timeout={Timeout}min, check interval={Interval}min.",
            _settings.IdleTimeoutMinutes, _settings.IdleCheckIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ShutdownIdleContainersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LightRAG idle shutdown service: error during idle check.");
            }
        }

        _logger.LogInformation("LightRAG idle shutdown service stopped.");
    }

    private async Task ShutdownIdleContainersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var portStore = scope.ServiceProvider.GetRequiredService<ILightRagAgentPortStore>();

        // Agents without a port reservation have no container yet, so only assigned ports matter.
        var assigned = await portStore.GetAssignedPortsAsync(ct);
        var timeout = TimeSpan.FromMinutes(_settings.IdleTimeoutMinutes);
        var now = DateTime.UtcNow;
        var shutdownCount = 0;

        foreach (var (agentId, _) in assigned)
        {
            var lastAccess = _accessTracker.GetLastAccess(agentId);

            // If we've never tracked access for this agent, don't shut it down —
            // it might have been just created or reconciled.
            if (lastAccess is null)
                continue;

            var idleDuration = now - lastAccess.Value;
            if (idleDuration < timeout)
                continue;

            _logger.LogInformation(
                "LightRAG idle shutdown: agent {AgentId} has been idle for {IdleMinutes:F0}min " +
                "(threshold={Threshold}min). Stopping container.",
                agentId, idleDuration.TotalMinutes, _settings.IdleTimeoutMinutes);

            try
            {
                await _containerManager.StopContainerAsync(agentId, ct);
                _accessTracker.Remove(agentId);
                shutdownCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "LightRAG idle shutdown: failed to stop container for agent {AgentId}.",
                    agentId);
            }
        }

        if (shutdownCount > 0)
        {
            _logger.LogInformation(
                "LightRAG idle shutdown: stopped {Count} idle container(s).", shutdownCount);
        }
    }
}
