using System.Security.Cryptography.X509Certificates;
using Docker.DotNet;
using Docker.DotNet.X509;
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
        // Each container serves HTTPS with the server's certificate; like the daemon channel
        // it is signed by a private CA we hold no root for, so the certificate is accepted
        // unvalidated — encrypted, but the server is unauthenticated. Requests remain gated by
        // the X-API-Key header, and inbound access by the cloud firewall (Azure NSG) rules.
        // When LightRag:SocksProxy is set, route through that SOCKS5 proxy instead (`ssh -D`),
        // so a developer can tunnel rather than open the port range; empty => direct.
        services.AddHttpClient(nameof(LightRagClient), c =>
        {
            c.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<LightRagSettings>>().Value;
#pragma warning disable CA5359 // Deliberate: the container certificate is signed by a private CA
            // whose root is not distributed to clients, so there is no trust anchor to validate
            // against. Traffic is encrypted but the server is unauthenticated; requests are gated
            // by X-API-Key and inbound access by the cloud firewall (Azure NSG) rules.
            var handler = new SocketsHttpHandler
            {
                SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true }
            };
#pragma warning restore CA5359
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
        // https://<server>:2376 => remote daemon over TLS) and injected so the manager is
        // testable. When a client certificate is configured the daemon is driven with mutual
        // TLS: it runs with `tlsverify: true` and admits only CA-signed client certificates.
        services.AddSingleton<IDockerClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<LightRagSettings>>().Value;
            var dockerConfig = string.IsNullOrWhiteSpace(cfg.DockerHost)
                ? new DockerClientConfiguration()
                : new DockerClientConfiguration(new Uri(cfg.DockerHost), BuildDockerCredentials(cfg));
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

    /// <summary>
    /// Credentials for the remote daemon: the configured PKCS#12 client certificate, or none when
    /// no certificate is set (an unauthenticated endpoint, as used in local dev).
    /// </summary>
    /// <remarks>
    /// The daemon's own certificate is deliberately NOT validated. It is signed by a private CA
    /// whose root is not distributed to clients, so there is no trust anchor to check it against;
    /// the connection is encrypted but the server is unauthenticated. Inbound access to :2376 is
    /// restricted by the cloud firewall (Azure NSG) instead.
    /// </remarks>
    private static CertificateCredentials? BuildDockerCredentials(LightRagSettings cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.DockerCertPath))
            return null;

        if (!File.Exists(cfg.DockerCertPath))
        {
            throw new FileNotFoundException(
                $"LightRag:DockerCertPath points at '{cfg.DockerCertPath}', which does not exist. " +
                "Copy the client certificate from the server (~/docker-certs/client.pfx) and set the path " +
                "in user-secrets.", cfg.DockerCertPath);
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(cfg.DockerCertPath, cfg.DockerCertPassword);
        var credentials = new CertificateCredentials(certificate);
#pragma warning disable CA5359 // Deliberate — see the remarks above.
        credentials.ServerCertificateValidationCallback += (_, _, _, _) => true;
#pragma warning restore CA5359
        return credentials;
    }
}
