namespace NTG.Agent.Orchestrator.Models.Configuration;

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

    // Network alias of the shared Postgres container on Aspire's Docker network.
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
    public int EmbeddingDim { get; set; } = 3072;
    public int ChunkSize { get; set; } = 1500;
    public int ChunkOverlap { get; set; } = 100;
    public int MaxAsync { get; set; } = 8;
    public int MaxParallelInsert { get; set; } = 2;
    public int EmbeddingFuncMaxAsync { get; set; } = 8;
}
