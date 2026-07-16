using NTG.Agent.Common.Dtos.Documents;

namespace NTG.Agent.LightRag;

/// <summary>A document whose ingestion is still in flight (Status == Processing with a track id).</summary>
public sealed record ProcessingDocument(Guid DocumentId, Guid AgentId, string TrackId);

/// <summary>Terminal status to record for a document once LightRAG resolves its ingestion.</summary>
public sealed record IngestionStatusUpdate(Guid DocumentId, DocumentStatus Status, string? KnowledgeDocId, string? ErrorMessage);

/// <summary>
/// Persistence seam for the ingestion-status worker
/// (<see cref="LightRagIngestionStatusHostedService"/>). The host application implements this
/// against its own document store so the LightRAG provider stays free of any EF/DbContext
/// dependency. Implementations are resolved from a scoped service provider.
/// </summary>
public interface ILightRagIngestionStore
{
    /// <summary>Documents still Processing that have a LightRAG track id to poll.</summary>
    Task<IReadOnlyList<ProcessingDocument>> GetProcessingDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies the resolved terminal statuses in one batch.</summary>
    Task ApplyUpdatesAsync(IReadOnlyList<IngestionStatusUpdate> updates, CancellationToken cancellationToken = default);
}
