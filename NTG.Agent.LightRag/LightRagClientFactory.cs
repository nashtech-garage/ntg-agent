using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Resolves a <see cref="LightRagClient"/> pointed at a specific agent's dedicated
/// container via the nginx gateway (<c>{GatewayUrl}/agents/{agentId}/</c>). The gateway
/// proxies that path to the <c>lightrag-agent-{agentId}</c> container by name on the Docker
/// network, which is what scopes every chat/upload call to the agent's own LightRAG
/// workspace. Scoped: caches resolved clients for the lifetime of the request scope.
/// <para>
/// If the container is not running (e.g. stopped by idle shutdown), this factory will
/// restart it via <see cref="ILightRagContainerManager.EnsureContainerAsync"/> before
/// creating the client.
/// </para>
/// </summary>
public sealed class LightRagClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILightRagContainerManager _containerManager;
    private readonly ILightRagHealthProbe _healthProbe;
    private readonly LightRagContainerAccessTracker _accessTracker;
    private readonly LightRagSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<Guid, LightRagClient> _cache = [];

    public LightRagClientFactory(
        IHttpClientFactory httpClientFactory,
        ILightRagContainerManager containerManager,
        ILightRagHealthProbe healthProbe,
        LightRagContainerAccessTracker accessTracker,
        IOptions<LightRagSettings> settings,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _containerManager = containerManager;
        _healthProbe = healthProbe;
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

        // Fast path: the gateway routes by container name, so a healthy answer on the agent's
        // path is provably this agent's own container — never another agent's. On a miss
        // (no container yet, or stopped by idle shutdown) ensure the container is running.
        // EnsureContainerAsync only returns once the container is serving (its readiness
        // gate), so the client below is safe to use.
        if (!await _healthProbe.IsHealthyAsync(agentId, cancellationToken))
        {
            await _containerManager.EnsureContainerAsync(agentId, cancellationToken);
        }

        // Named client inherits the standard resilience handler with the LightRAGClient
        // overrides (2-min attempt timeout, no retries) and the TLS settings configured in
        // AddLightRagKnowledge. The trailing slash keeps the /agents/{id} prefix when the
        // client's relative request paths are resolved against it.
        var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
        http.BaseAddress = new Uri($"{_settings.ResolveGatewayUrl()}/agents/{agentId}/");
        if (!string.IsNullOrEmpty(_settings.ApiKey))
            http.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);

        var client = new LightRagClient(http, _loggerFactory.CreateLogger<LightRagClient>());
        _cache[agentId] = client;
        _accessTracker.Touch(agentId);
        return client;
    }
}
