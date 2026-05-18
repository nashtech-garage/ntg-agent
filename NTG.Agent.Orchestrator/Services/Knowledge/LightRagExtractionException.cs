namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class LightRagExtractionException : Exception
{
    public string? DocId { get; }
    public string FileName { get; }

    public LightRagExtractionException(string? docId, string fileName, string message) : base(message)
    {
        DocId = docId;
        FileName = fileName;
    }
}
