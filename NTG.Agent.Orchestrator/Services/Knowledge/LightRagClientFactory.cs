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
/// <para>
/// If the container is not running (e.g. stopped by idle shutdown), this factory will
/// restart it via <see cref="ILightRagContainerManager.EnsureContainerAsync"/> and update
/// the stored port before creating the client.
/// </para>
/// </summary>
public sealed class LightRagClientFactory
{
    private readonly AgentDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILightRagContainerManager _containerManager;
    private readonly LightRagContainerAccessTracker _accessTracker;
    private readonly LightRagSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<Guid, LightRagClient> _cache = [];

    public LightRagClientFactory(
        AgentDbContext db,
        IHttpClientFactory httpClientFactory,
        ILightRagContainerManager containerManager,
        LightRagContainerAccessTracker accessTracker,
        IOptions<LightRagSettings> settings,
        ILoggerFactory loggerFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _containerManager = containerManager;
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

        var port = await _db.Agents
            .Where(a => a.Id == agentId)
            .Select(a => a.LightRagPort)
            .FirstOrDefaultAsync(cancellationToken);

        // If the port is missing or the container is not reachable, ensure it is running.
        // This handles the case where idle shutdown stopped the container.
        if (port is not > 0 || !await IsContainerReachableAsync(port.Value))
        {
            port = await _containerManager.EnsureContainerAsync(agentId, port, cancellationToken);

            // Persist the (possibly new) port so subsequent requests skip the health check.
            var agent = await _db.Agents.FindAsync(new object[] { agentId }, cancellationToken);
            if (agent is not null && agent.LightRagPort != port)
            {
                agent.LightRagPort = port;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // Named client inherits the standard resilience handler with the LightRAGClient
        // overrides (2-min attempt timeout, no retries) configured in Program.cs.
        var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
        http.BaseAddress = new Uri($"http://localhost:{port}");
        if (!string.IsNullOrEmpty(_settings.ApiKey))
            http.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);

        var client = new LightRagClient(http, _loggerFactory.CreateLogger<LightRagClient>());
        _cache[agentId] = client;
        _accessTracker.Touch(agentId);
        return client;
    }

    /// <summary>
    /// Quick TCP check to see if a container is accepting connections on the given port.
    /// Used to detect containers that were stopped by idle shutdown.
    /// </summary>
    private static async Task<bool> IsContainerReachableAsync(int port)
    {
        try
        {
            using var tcpClient = new System.Net.Sockets.TcpClient();
            var connectTask = tcpClient.ConnectAsync(System.Net.IPAddress.Loopback, port);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(connectTask, timeoutTask);
            return completed == connectTask && tcpClient.Connected;
        }
        catch
        {
            return false;
        }
    }
}