using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

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
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

        var agents = await db.Agents.ToListAsync(ct);
        var timeout = TimeSpan.FromMinutes(_settings.IdleTimeoutMinutes);
        var now = DateTime.UtcNow;
        var shutdownCount = 0;

        foreach (var agent in agents)
        {
            // Skip agents that don't have a container yet.
            if (agent.LightRagPort is null or 0)
                continue;

            var lastAccess = _accessTracker.GetLastAccess(agent.Id);

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
                agent.Id, idleDuration.TotalMinutes, _settings.IdleTimeoutMinutes);

            try
            {
                await _containerManager.StopContainerAsync(agent.Id, ct);
                _accessTracker.Remove(agent.Id);
                shutdownCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "LightRAG idle shutdown: failed to stop container for agent {AgentId}.",
                    agent.Id);
            }
        }

        if (shutdownCount > 0)
        {
            _logger.LogInformation(
                "LightRAG idle shutdown: stopped {Count} idle container(s).", shutdownCount);
        }
    }
}