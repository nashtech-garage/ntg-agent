using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public interface IKnowledgeService
{
    public Task<SearchResult> SearchAsync(string query, Guid agentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default);

    Task<SearchResult> SearchPerConversationAsync(string query, Guid conversationId, CancellationToken cancellationToken = default);

    public Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default);

    public Task<string> ImportWebPageAsync(string url, Guid conversationId, CancellationToken cancellationToken = default);

    public Task<string> ImportWebPageAsync(string url, Guid agentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task<string> ImportTextContentAsync(string content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default);

    public Task<StreamableFileContent> ExportDocumentAsync(string documentId, string fileName, Guid agentId, CancellationToken cancellationToken = default);

    Task ClearDocumentsPerConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
