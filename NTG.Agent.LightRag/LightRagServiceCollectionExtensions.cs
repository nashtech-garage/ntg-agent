using Docker.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NTG.Agent.Common.Knowledge;

namespace NTG.Agent.LightRag;

/// <summary>
/// Single entry point for hosting the LightRAG knowledge provider. The host only calls
/// <see cref="AddLightRagKnowledge"/> and implements the two persistence seams
/// (<see cref="ILightRagAgentPortStore"/>, <see cref="ILightRagIngestionStore"/>);
/// everything else — per-agent containers, port reservations, HTTP clients, background
/// workers — is wired here.
/// </summary>
public static class LightRagServiceCollectionExtensions
{
    public static IServiceCollection AddLightRagKnowledge(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LightRagSettings>(configuration.GetSection("LightRag"));

        services.AddScoped<IKnowledgeService, LightRagKnowledge>();
        services.AddScoped<IKnowledgeProvisioner, LightRagKnowledgeProvisioner>();

        services.AddSingleton<LightRagFileStore>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<LightRagSettings>>().Value;
            var log = sp.GetRequiredService<ILogger<LightRagFileStore>>();
            return new LightRagFileStore(cfg.FileStorePath, log);
        });

        // LightRAG /query invokes an LLM and routinely takes >10s, exceeding the
        // standard resilience handler's 10s per-attempt timeout. Override the named
        // options for this client so the host's global resilience pipeline is reused
        // with longer timeouts and no retries (retrying slow LLM calls is wasteful).
        services.Configure<HttpStandardResilienceOptions>(
            nameof(LightRagClient),
            o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                o.Retry.MaxRetryAttempts = 0;
            });

        // Named LightRAG HTTP client — BaseAddress + X-API-Key are set per agent by
        // LightRagClientFactory (each agent has its own container endpoint), so we only
        // configure the timeout here. The resilience override above is keyed on this name.
        // When LightRag:SocksProxy is set, route through that SOCKS5 proxy so the dynamic
        // per-agent container ports are reachable over the SSH tunnel (`ssh -D`); empty =>
        // direct connection (local dev).
        services.AddHttpClient(nameof(LightRagClient), c =>
        {
            c.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<LightRagSettings>>().Value;
            var handler = new SocketsHttpHandler();
            if (!string.IsNullOrWhiteSpace(cfg.SocksProxy))
            {
                handler.Proxy = new System.Net.WebProxy(cfg.SocksProxy);
                handler.UseProxy = true;
            }
            return handler;
        });

        // One LightRAG container per agent: the manager owns the Docker lifecycle, the
        // factory resolves a per-agent client, and the reconciler ensures containers exist
        // for every agent on startup.
        // IDockerClient is built from LightRagSettings.DockerHost (empty => local socket;
        // tcp://<server>:2375 => remote daemon) and injected so the manager is testable.
        services.AddSingleton<IDockerClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<LightRagSettings>>().Value;
            var dockerConfig = string.IsNullOrWhiteSpace(cfg.DockerHost)
                ? new DockerClientConfiguration()
                : new DockerClientConfiguration(new Uri(cfg.DockerHost));
            return dockerConfig.CreateClient();
        });
        // Shared readiness probe: used by the container manager to poll a freshly-started
        // container until its app serves, and by the client factory's fast-path reachability
        // check. Singleton — it is stateless and only builds named HTTP clients on demand.
        services.AddSingleton<ILightRagHealthProbe, LightRagHealthProbe>();
        services.AddSingleton<ILightRagContainerManager, LightRagContainerManager>();
        services.AddSingleton<LightRagContainerAccessTracker>();
        // Identity-bound host-port reservations (one permanent port per agent) — prevents
        // cross-agent misrouting when a freed port would otherwise be recycled. The provisioner
        // centralises the reserve->ensure->reassign flow used by the factory, reconciler, and
        // agent creation.
        // Allocation is arbitrated by the shared Postgres ledger rather than the local database, so
        // developers sharing one Docker host cannot hand out the same port (see
        // deploy/lightrag-postgres/migrations/001_create_agent_port_reservations.sql).
        services.AddScoped<ILightRagPortReservationStore, LightRagPgPortReservationStore>();
        services.AddScoped<PortReservationService>();
        services.AddScoped<ILightRagProvisioner, LightRagProvisioner>();
        services.AddScoped<LightRagClientFactory>();
        services.AddHostedService<LightRagReconcilerHostedService>();
        services.AddHostedService<LightRagContainerIdleShutdownService>();
        // Event-driven worker: parks while idle and wakes (via IngestionStatusSignal) when an upload
        // begins, polling LightRAG until every Processing document reaches Completed/Failed.
        services.AddHostedService<LightRagIngestionStatusHostedService>();

        return services;
    }
}
