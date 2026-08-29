using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Resolves a <see cref="LightRagClient"/> pointed at the container backing an agent's knowledge
/// base, via the nginx gateway (<c>{GatewayUrl}/agents/{ownerAgentId}/</c>). The gateway proxies
/// that path to the <c>lightrag-agent-{ownerAgentId}</c> container by name on the Docker network.
/// <para>
/// The path carries the id of the agent that <i>owns</i> the knowledge base, not necessarily the
/// calling agent: agents sharing a knowledge base share one container and one workspace, and every
/// one of them dials the owner's path. An agent that owns its knowledge base resolves to itself.
/// </para>
/// <para>
/// Scoped: caches resolved clients for the lifetime of the request scope, keyed by owner so two
/// agents in the same knowledge base share one client.
/// </para>
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
    private readonly LightRagWorkspaceResolver _resolver;
    private readonly LightRagSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<Guid, LightRagClient> _cache = [];

    public LightRagClientFactory(
        IHttpClientFactory httpClientFactory,
        ILightRagContainerManager containerManager,
        ILightRagHealthProbe healthProbe,
        LightRagContainerAccessTracker accessTracker,
        LightRagWorkspaceResolver resolver,
        IOptions<LightRagSettings> settings,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _containerManager = containerManager;
        _healthProbe = healthProbe;
        _accessTracker = accessTracker;
        _resolver = resolver;
        _settings = settings.Value;
        _loggerFactory = loggerFactory;
    }

    public async Task<LightRagClient> GetClientAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        // Everything below is keyed on the knowledge base, not the caller: agents sharing one
        // dial the same path and reuse the same client. Throws for an agent with no knowledge
        // base (an inner agent), which has no container to dial.
        var ownerAgentId = await _resolver.RequireOwnerAgentIdAsync(agentId, cancellationToken);

        if (_cache.TryGetValue(ownerAgentId, out var cached))
        {
            _accessTracker.Touch(ownerAgentId);
            return cached;
        }

        // Fast path: the gateway routes by container name, so a healthy answer on the knowledge
        // base's path is provably that knowledge base's own container — never another's. On a miss
        // (no container yet, or stopped by idle shutdown) ensure the container is running.
        // EnsureContainerAsync only returns once the container is serving (its readiness
        // gate), so the client below is safe to use.
        if (!await _healthProbe.IsHealthyAsync(ownerAgentId, cancellationToken))
        {
            await _containerManager.EnsureContainerAsync(ownerAgentId, cancellationToken);
        }

        // Named client inherits the standard resilience handler with the LightRAGClient
        // overrides (2-min attempt timeout, no retries) and the TLS settings configured in
        // AddLightRagKnowledge. The trailing slash keeps the /agents/{id} prefix when the
        // client's relative request paths are resolved against it.
        var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
        http.BaseAddress = new Uri($"{_settings.ResolveGatewayUrl()}/agents/{ownerAgentId}/");
        if (!string.IsNullOrEmpty(_settings.ApiKey))
            http.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);

        var client = new LightRagClient(http, _loggerFactory.CreateLogger<LightRagClient>());
        _cache[ownerAgentId] = client;
        _accessTracker.Touch(ownerAgentId);
        return client;
    }
}
