namespace NTG.Agent.Orchestrator.Models.Configuration;

public class LightRagSettings
{
    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    // NOTE: Replace with Azure Blob Storage for production deployment.
    public string FileStorePath { get; set; } = "./lightrag-filestore";

    public int TopK { get; set; } = 60;
}
