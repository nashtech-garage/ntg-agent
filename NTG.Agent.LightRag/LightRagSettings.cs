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

    public int TopK { get; set; } = 60;

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

    // ---- Remote Docker host (SSH tunnel) ------------------------------------
    // The LightRAG stack (Postgres + the per-agent containers) lives on a separate
    // Ubuntu server reached over an SSH tunnel. The Orchestrator forwards the Docker
    // socket and Postgres with `ssh -L`, and reaches the dynamic per-agent container
    // ports through an `ssh -D` SOCKS proxy. Empty / loopback defaults preserve the
    // original all-local behaviour.

    // Docker daemon endpoint the manager drives. Empty => local socket
    // (npipe/unix) via DockerClientConfiguration's default. SSH-tunnel example:
    // "tcp://localhost:2375" (a forwarded `ssh -L 2375:/var/run/docker.sock`).
    public string DockerHost { get; set; } = string.Empty;

    // Host the Orchestrator dials to reach a container's published HTTP port (and,
    // by fallback, Postgres). Over the SSH tunnel this stays "localhost": the SOCKS
    // proxy resolves it on the server side, so it means the server's loopback.
    public string ServerHost { get; set; } = "localhost";

    // IP the container's port is published on (HostConfig.PortBindings HostIP).
    // Bound to the server's loopback (127.0.0.1); the Orchestrator reaches it through
    // the SSH SOCKS proxy, so it is never exposed on a public interface.
    public string PortBindHostIp { get; set; } = "127.0.0.1";

    // SOCKS5 proxy the LightRAG HTTP client routes through to reach the dynamic
    // per-agent container ports over the SSH tunnel (`ssh -D`). Empty => no proxy
    // (direct connection for local dev). SSH-tunnel example: "socks5://localhost:1080".
    public string SocksProxy { get; set; } = string.Empty;

    // Direct Postgres connection used ONLY by ResetVectorSchemaAsync (reached over a
    // forwarded `ssh -L 5432:127.0.0.1:5432`). Empty PostgresHost => fall back to ServerHost.
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

    // ---- Idle container shutdown ------------------------------------------------
    // When a per-agent LightRAG container has not received a request for this many
    // minutes, it is stopped to reclaim RAM. Set to 0 or negative to disable.
    public int IdleTimeoutMinutes { get; set; } = 30;

    // How often the idle-shutdown background service checks for stale containers.
    public int IdleCheckIntervalMinutes { get; set; } = 5;
}
