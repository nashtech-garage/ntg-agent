namespace NTG.Agent.Common.Dtos.Documents;

/// <summary>
/// Tracks where a document is in the LightRAG ingestion pipeline. Persisted on the Document
/// row so the UI can show "Uploading" / "Uploaded" / "Failed to upload" without blocking the
/// upload request on LightRAG's (open-ended) extraction.
/// </summary>
public enum DocumentStatus
{
    Processing = 1,
    Completed = 2,
    Failed = 3
}
