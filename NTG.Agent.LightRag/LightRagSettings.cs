namespace NTG.Agent.LightRag;

public class LightRagSettings
{
    // ---- Legacy single-endpoint field ---------------------------------------
    // Retained for backwards-compatibility / tooling, but the orchestrator now
    // resolves a per-agent endpoint (http://localhost:{Agent.LightRagPort}) via
    // LightRagClientFactory instead of talking to one shared endpoint.
    public string Endpoint { get; set; } = string.Empty;

    // Gates the LightRAG HTTP API (X-API-Key). Shared by every per-agent container.
    public string ApiKey { get; set; } = string.Empty;

    // NOTE: Replace with Azure Blob Storage for production deployment.
    public string FileStorePath { get; set; } = "./lightrag-filestore";

    public int TopK { get; set; } = 50;

    // LightRAG /query retrieval mode: "naive" (vector only), "local" (entity graph),
    // "global", "hybrid" (local + global), or "mix". Config-driven so it can be tuned
    public string QueryMode { get; set; } = "hybrid";

    // How long to wait for LightRAG's async ingestion pipeline to finish before bailing.
    // Large docs at the current 8x parallelism finish in 1-2 min; 3 min default leaves headroom.
    public int UploadTimeoutSeconds { get; set; } = 180;

    public int PollIntervalSeconds { get; set; } = 3;

    // ---- Per-agent container provisioning -----------------------------------
    // Everything below is consumed by LightRagContainerManager to spin up a
    // dedicated lightrag-agent-{agentId} app container per agent, all pointed at
    // the single shared lightrag-postgres and isolated by WORKSPACE={agentId}.

    public string ImageRef { get; set; } = "ghcr.io/hkuds/lightrag";
    public string ImageTag { get; set; } = "v1.4.16";

    // ---- Remote Docker host (TLS) -------------------------------------------
    // The LightRAG stack (Postgres + the per-agent containers) lives on a separate
    // Ubuntu server, reached directly over TLS — no SSH tunnel. Three channels:
    //   * the Docker daemon on :2376, authenticated with a client certificate (mutual TLS);
    //   * each per-agent container's HTTPS port in the reserved range;
    //   * Postgres on :5432 with SSL Mode=Require.
    // Empty / loopback defaults preserve the original all-local behaviour.

    // Docker daemon endpoint the manager drives. Empty => local socket
    // (npipe/unix) via DockerClientConfiguration's default. Remote TLS example:
    // "https://4.193.109.6:2376" — requires DockerCertPath below.
    public string DockerHost { get; set; } = string.Empty;

    // PKCS#12 bundle (client cert + private key) presented to the daemon, which runs with
    // `tlsverify: true` and admits only certificates signed by its CA. Empty => no client
    // certificate, i.e. a plain local socket or an unauthenticated endpoint.
    public string DockerCertPath { get; set; } = string.Empty;

    // Password protecting DockerCertPath. Secret — supply via user-secrets, never appsettings.
    public string DockerCertPassword { get; set; } = string.Empty;

    // Host the Orchestrator dials to reach a container's published HTTPS port (and,
    // by fallback, Postgres). The server's public address, e.g. "4.193.109.6".
    public string ServerHost { get; set; } = "localhost";

    // IP the container's port is published on (HostConfig.PortBindings HostIP).
    // "0.0.0.0" publishes on every interface so the Orchestrator can dial the port
    // directly; inbound access is gated by the cloud firewall (Azure NSG) rules.
    public string PortBindHostIp { get; set; } = "127.0.0.1";

    // Directory on the SERVER holding the TLS certificate and key the per-agent containers
    // serve HTTPS with. Bind-mounted read-only into each container at CertMountPath. Empty
    // => containers serve plain HTTP (local dev).
    public string ServerCertDirectory { get; set; } = string.Empty;

    // Mount point for ServerCertDirectory inside each spawned container. The SSL_CERTFILE /
    // SSL_KEYFILE paths handed to LightRAG are resolved against it.
    public string CertMountPath { get; set; } = "/certs";

    // Certificate and key filenames within CertMountPath, as seen inside the container.
    public string ServerCertFileName { get; set; } = "server-cert.pem";
    public string ServerKeyFileName { get; set; } = "server-key.pem";

    // Optional SOCKS5 proxy for the LightRAG HTTP client. Retained so a developer can still
    // tunnel (`ssh -D 1080` => "socks5://localhost:1080") instead of opening the port range.
    // Empty => direct connection, which is the TLS default.
    public string SocksProxy { get; set; } = string.Empty;

    // Direct Postgres connection used by ResetVectorSchemaAsync and the port-reservation
    // ledger. Empty PostgresHost => fall back to ServerHost.
    public string PostgresHost { get; set; } = string.Empty;
    public int PostgresPort { get; set; } = 5432;

    // ---- Reserved host-port pool (identity-bound ports) ---------------------
    // Each agent permanently owns one host port from this inclusive range; a port
    // is never recycled to a different agent, so a reachable reserved port is
    // provably that agent's own container. This prevents cross-agent misrouting
    // when idle-shutdown frees a port and a recreate would otherwise reuse it.
    public int PortRangeStart { get; set; } = 20000;
    public int PortRangeEnd { get; set; } = 20999;

    // Network alias of the shared Postgres container on the Docker network.
    // Used internally by the spawned containers (POSTGRES_HOST) — unchanged by
    // the move, since they resolve it over the server-side Docker network.
    public string PostgresHostAlias { get; set; } = "lightrag-postgres";
    public string PostgresPassword { get; set; } = string.Empty;
    public string PostgresDatabase { get; set; } = "uploaded-documents";

    // Azure OpenAI bindings (mirrors what the old singleton lightrag container used).
    public string LlmModel { get; set; } = "gpt-5.4";
    public string LlmEndpoint { get; set; } = string.Empty;
    public string LlmApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";
    public string EmbeddingEndpoint { get; set; } = string.Empty;
    public string EmbeddingApiKey { get; set; } = string.Empty;
    public string AzureApiVersion { get; set; } = "2024-08-01-preview";

    // Ingestion tuning knobs (mirror the old AppHost env wiring).
    public int EmbeddingDim { get; set; } = 1536;
    // Instructs LightRAG to pass 'dimensions' to the Azure OpenAI embedding API so it truncates
    // to EmbeddingDim via MRL. Required when EmbeddingDim < the model's native dimension (3072
    // for text-embedding-3-large). Without this, LightRAG skips 'dimensions', Azure returns full
    // 3072-dim vectors, and the count mismatch (expected N, got 2×N) occurs.
    public bool EmbeddingSendDim { get; set; } = true;
    public int ChunkSize { get; set; } = 1500;
    public int ChunkOverlap { get; set; } = 100;
    public int MaxAsync { get; set; } = 8;
    public int MaxParallelInsert { get; set; } = 2;
    public int EmbeddingFuncMaxAsync { get; set; } = 8;

    // ---- Container readiness gate -----------------------------------------------
    // After a container is started, its published host port is bound immediately, but the
    // LightRAG ASGI app inside is still booting (Postgres/pgvector init). A request sent in
    // that window is dropped ("response ended prematurely"). EnsureContainerAsync polls
    // GET /health until the app answers before returning, up to this budget; on expiry it
    // throws LightRagContainerNotReadyException rather than hand back a not-serving endpoint.
    public int ReadinessTimeoutSeconds { get; set; } = 60;

    // How often the readiness gate re-probes /health while waiting for the app to come up.
    public int ReadinessPollIntervalMs { get; set; } = 500;

    // ---- Docker daemon readiness (startup) --------------------------------------
    // The daemon is reached over TLS (https://<server>:2376). If the Orchestrator boots while
    // the server is still unreachable — a restarting daemon, a firewall rule not yet applied —
    // the startup reconciler polls for reachability up to this budget before giving up.
    // Default 60s ≈ "retry within 1 minute, then fail".
    public int DaemonProbeTimeoutSeconds { get; set; } = 60;

    // How often the reconciler re-pings the daemon while waiting for it to come up.
    public int DaemonProbePollIntervalMs { get; set; } = 2000;

    // ---- Idle container shutdown ------------------------------------------------
    // When a per-agent LightRAG container has not received a request for this many
    // minutes, it is stopped to reclaim RAM. Set to 0 or negative to disable.
    public int IdleTimeoutMinutes { get; set; } = 30;

    // How often the idle-shutdown background service checks for stale containers.
    public int IdleCheckIntervalMinutes { get; set; } = 5;
}
