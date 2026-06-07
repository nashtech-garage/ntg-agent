using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.Common.Dtos.Knowledge;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Result of polling an in-flight ingestion. <see cref="KnowledgeDocId"/> is populated once the
/// pipeline assigns a real doc-id (i.e. when <see cref="Status"/> is
/// <see cref="DocumentStatus.Completed"/>); <see cref="ErrorMessage"/> is set on
/// <see cref="DocumentStatus.Failed"/>.
/// </summary>
public sealed record DocumentIngestResult(DocumentStatus Status, string? KnowledgeDocId, string? ErrorMessage);

public interface IKnowledgeService
{
    public Task<KnowledgeSearchResponse> SearchAsync(string query, Guid agentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task<KnowledgeSearchResponse> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default);

    // Begin* methods are non-blocking: they hand the content to the knowledge backend and return a
    // tracking handle (LightRAG track_id) immediately. Use CheckIngestStatusAsync to resolve the
    // final status and doc-id later. documentId is the local Document.Id, used to key the file store.
    public Task<string> BeginImportDocumentAsync(Stream content, string fileName, Guid agentId, Guid documentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task<string> BeginImportWebPageAsync(string url, Guid agentId, Guid documentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task<string> BeginImportTextContentAsync(string content, string fileName, Guid agentId, Guid documentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task<DocumentIngestResult> CheckIngestStatusAsync(Guid agentId, string trackId, CancellationToken cancellationToken = default);

    public Task RemoveDocumentAsync(Guid agentId, Guid documentId, string? knowledgeDocId, string? trackId, CancellationToken cancellationToken = default);

    public Task<KnowledgeFileContent> ExportDocumentAsync(Guid agentId, Guid documentId, string? knowledgeDocId, string fileName, CancellationToken cancellationToken = default);
}
