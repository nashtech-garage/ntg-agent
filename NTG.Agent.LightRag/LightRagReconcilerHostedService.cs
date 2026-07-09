using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagReconcilerHostedService> _logger;

    public LightRagReconcilerHostedService(
        IServiceProvider serviceProvider,
        ILightRagContainerManager containerManager,
        IOptions<LightRagSettings> settings,
        ILogger<LightRagReconcilerHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _containerManager = containerManager;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The daemon is reached over an SSH tunnel; if the app booted before the tunnel was
            // up the daemon is momentarily unreachable. Wait for it (up to the budget) instead of
            // failing on the single startup attempt — otherwise no containers are reconciled until
            // a full app restart. Give up with a clear failure once the budget expires.
            if (!await WaitForDaemonAsync(stoppingToken))
            {
                _logger.LogError(
                    "LightRAG reconciler: Docker daemon at '{DockerHost}' still unreachable after {Timeout}s — " +
                    "is the SSH tunnel (ssh -L 2375:/var/run/docker.sock) up? Skipping startup reconciliation; " +
                    "containers will be provisioned on demand once the daemon is reachable.",
                    string.IsNullOrWhiteSpace(_settings.DockerHost) ? "local socket" : _settings.DockerHost,
                    Math.Max(1, _settings.DaemonProbeTimeoutSeconds));
                return;
            }

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

    // Polls the daemon until it answers or the budget expires. Returns true as soon as it is
    // reachable, false once the budget is exhausted. Fast path: returns on the first probe when
    // the tunnel is already up.
    private async Task<bool> WaitForDaemonAsync(CancellationToken stoppingToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.DaemonProbeTimeoutSeconds));
        var interval = TimeSpan.FromMilliseconds(Math.Max(50, _settings.DaemonProbePollIntervalMs));
        var deadline = DateTime.UtcNow + timeout;
        var warned = false;

        while (true)
        {
            if (await _containerManager.IsDaemonReachableAsync(stoppingToken))
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            if (!warned)
            {
                _logger.LogWarning(
                    "LightRAG reconciler: Docker daemon at '{DockerHost}' not reachable yet; " +
                    "waiting up to {Timeout}s for the SSH tunnel to come up...",
                    string.IsNullOrWhiteSpace(_settings.DockerHost) ? "local socket" : _settings.DockerHost,
                    Math.Max(1, _settings.DaemonProbeTimeoutSeconds));
                warned = true;
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
