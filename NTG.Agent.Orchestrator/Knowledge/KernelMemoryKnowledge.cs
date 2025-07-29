using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly MemoryWebClient _memoryWebClient;

    public KernelMemoryKnowledge()
    {
        _memoryWebClient = new MemoryWebClient("https://localhost:7181", "Blm8d7sFx7arM9EN2QUxGy7yUjCyvRjx");
    }
    public async Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, CancellationToken cancellationToken = default)
    {
        return await _memoryWebClient.ImportDocumentAsync(content, fileName);
    }

    public async Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default)
    {
        await _memoryWebClient.DeleteDocumentAsync(documentId);
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, CancellationToken cancellationToken = default)
    {
        var result = await _memoryWebClient.SearchAsync(query, limit: 3);
        return result;
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _memoryWebClient.SearchAsync(query);
        return result;
    }

    public async Task<string> ImportWebPageAsync(string url, Guid agentId, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Invalid URL provided.", nameof(url));
        }

        var documentId = await _memoryWebClient.ImportWebPageAsync(url, cancellationToken: cancellationToken);
        return documentId;
    }
}
