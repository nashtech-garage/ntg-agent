namespace NTG.Agent.Orchestrator.Models.Configuration;

public class LightRagSettings
{
    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    // NOTE: Replace with Azure Blob Storage for production deployment.
    public string FileStorePath { get; set; } = "./lightrag-filestore";

    public int TopK { get; set; } = 60;

    // How long to wait for LightRAG's async ingestion pipeline to finish before bailing.
    // Large docs at the current 8x parallelism finish in 1-2 min; 3 min default leaves headroom.
    public int UploadTimeoutSeconds { get; set; } = 180;

    public int PollIntervalSeconds { get; set; } = 3;
}
