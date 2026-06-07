using NTG.Agent.Common.Dtos.Documents;

namespace NTG.Agent.Orchestrator.Models.Documents;

public class Document
{
    public Document()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? KnowledgeDocId { get; set; }
    public Guid? FolderId { get; set; }
    public Guid AgentId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DocumentType Type { get; set; } = DocumentType.File;

    /// <summary>Where this document is in the LightRAG ingestion pipeline. New uploads start as
    /// <see cref="DocumentStatus.Processing"/>; the background worker advances it.</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;

    /// <summary>LightRAG track_id returned when ingestion begins; used to poll for completion.</summary>
    public string? TrackId { get; set; }

    /// <summary>Failure reason surfaced to the UI when <see cref="Status"/> is <see cref="DocumentStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }
    
    // Navigation properties
    public ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();
}
