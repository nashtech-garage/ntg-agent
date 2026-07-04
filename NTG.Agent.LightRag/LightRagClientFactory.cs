using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Resolves a <see cref="LightRagClient"/> pointed at a specific agent's dedicated
/// container (<c>http://localhost:{port}</c> from the agent's port reservation). This is what
/// scopes every chat/upload call to the agent's own LightRAG workspace instead of a shared
/// endpoint. Scoped: caches resolved clients for the lifetime of the request scope.
/// <para>
/// If the container is not running (e.g. stopped by idle shutdown), this factory will
/// restart it via <see cref="ILightRagContainerManager.EnsureContainerAsync"/> and update
/// the stored port before creating the client.
/// </para>
/// </summary>
public sealed class LightRagClientFactory
{
    private readonly ILightRagAgentPortStore _portStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILightRagProvisioner _provisioner;
    private readonly LightRagContainerAccessTracker _accessTracker;
    private readonly LightRagSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<Guid, LightRagClient> _cache = [];

    public LightRagClientFactory(
        ILightRagAgentPortStore portStore,
        IHttpClientFactory httpClientFactory,
        ILightRagProvisioner provisioner,
        LightRagContainerAccessTracker accessTracker,
        IOptions<LightRagSettings> settings,
        ILoggerFactory loggerFactory)
    {
        _portStore = portStore;
        _httpClientFactory = httpClientFactory;
        _provisioner = provisioner;
        _accessTracker = accessTracker;
        _settings = settings.Value;
        _loggerFactory = loggerFactory;
    }

    public async Task<LightRagClient> GetClientAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(agentId, out var cached))
        {
            _accessTracker.Touch(agentId);
            return cached;
        }

        var port = await _portStore.GetPortAsync(agentId, cancellationToken);

        // Fast path: the port is identity-bound (reserved exclusively to this agent), so if
        // something answers on it, it is provably this agent's own container — never another
        // agent's. On a miss (no reservation yet, or stopped by idle shutdown) ensure the
        // container is running on the agent's reserved port.
        if (port is not > 0 || !await IsContainerReachableAsync(port.Value))
        {
            port = await _provisioner.ProvisionAsync(agentId, cancellationToken);
        }

        // Named client inherits the standard resilience handler with the LightRAGClient
        // overrides (2-min attempt timeout, no retries) and the SOCKS proxy configured in
        // AddLightRagKnowledge (so the container is reached through the SSH tunnel when set).
        var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
        http.BaseAddress = new Uri($"http://{ResolveHost()}:{port}");
        if (!string.IsNullOrEmpty(_settings.ApiKey))
            http.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);

        var client = new LightRagClient(http, _loggerFactory.CreateLogger<LightRagClient>());
        _cache[agentId] = client;
        _accessTracker.Touch(agentId);
        return client;
    }

    private string ResolveHost() => string.IsNullOrWhiteSpace(_settings.ServerHost) ? "localhost" : _settings.ServerHost;

    /// <summary>
    /// Quick check that a container is up on the given port. Uses an HTTP request through the
    /// named client (so it traverses the SOCKS proxy / SSH tunnel when configured, which a raw
    /// TCP connect cannot). Any HTTP response — even 401/404 — means the container answered;
    /// only a connection-level failure or timeout counts as unreachable (e.g. stopped by idle
    /// shutdown).
    /// </summary>
    private async Task<bool> IsContainerReachableAsync(int port)
    {
        try
        {
            var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
            http.BaseAddress = new Uri($"http://{ResolveHost()}:{port}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var _ = await http.GetAsync("health", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
