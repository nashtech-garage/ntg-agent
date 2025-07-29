using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Knowledge;

public interface IKnowledgeService
{
    public Task<SearchResult> SearchAsync(string query, Guid agentId, CancellationToken cancellationToken = default);

    public Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default);

    public Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, CancellationToken cancellationToken = default);

    public Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default);

    public Task<string> ImportWebPageAsync(string url, Guid agentId, CancellationToken cancellationToken = default);
}
