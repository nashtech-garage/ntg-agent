using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Resolves a <see cref="LightRagClient"/> pointed at a specific agent's dedicated
/// container (<c>http://localhost:{Agent.LightRagPort}</c>). This is what scopes every
/// chat/upload call to the agent's own LightRAG workspace instead of a shared endpoint.
/// Scoped: caches resolved clients for the lifetime of the request scope.
/// </summary>
public sealed class LightRagClientFactory
{
    private readonly AgentDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LightRagSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<Guid, LightRagClient> _cache = [];

    public LightRagClientFactory(
        AgentDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<LightRagSettings> settings,
        ILoggerFactory loggerFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _loggerFactory = loggerFactory;
    }

    public async Task<LightRagClient> GetClientAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(agentId, out var cached))
            return cached;

        var port = await _db.Agents
            .Where(a => a.Id == agentId)
            .Select(a => a.LightRagPort)
            .FirstOrDefaultAsync(cancellationToken);

        if (port is not > 0)
            throw new InvalidOperationException(
                $"Agent '{agentId}' has no provisioned LightRAG container yet (LightRagPort is not set).");

        // Named client inherits the standard resilience handler with the LightRagClient
        // overrides (2-min attempt timeout, no retries) configured in Program.cs.
        var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
        http.BaseAddress = new Uri($"http://localhost:{port}");
        if (!string.IsNullOrEmpty(_settings.ApiKey))
            http.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);

        var client = new LightRagClient(http, _loggerFactory.CreateLogger<LightRagClient>());
        _cache[agentId] = client;
        return client;
    }
}
