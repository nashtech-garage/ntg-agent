using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly MemoryWebClient _memoryWebClient;

    public KernelMemoryKnowledge()
    {
        _memoryWebClient = new MemoryWebClient("https://localhost:7181", "Blm8d7sFx7arM9EN2QUxGy7yUjCyvRjx");
    }
    public async Task<string> ImportDocumentAsync(Stream content, string fileName, bool isPublicAccess, Guid agentId, CancellationToken cancellationToken = default)
    {
        var tags = new TagCollection();
        tags["access"] = isPublicAccess ? ["public"] : ["private"];
        return await _memoryWebClient.ImportDocumentAsync(content, fileName,tags: tags);
    }

    public async Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default)
    {
        await _memoryWebClient.DeleteDocumentAsync(documentId);
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, bool isSignedIn, CancellationToken cancellationToken = default)
    {
        SearchResult result;
        if (isSignedIn)
        {
            var filters = new List<MemoryFilter>
            {
                MemoryFilters.ByTag("access", "public"),
                MemoryFilters.ByTag("access", "private")
            };
            result = await _memoryWebClient.SearchAsync(
                query: query,
                filters: filters,
                limit: 3,
                cancellationToken: cancellationToken);
        }
        else
        {
            var filter = MemoryFilters.ByTag("access", "public");
            result = await _memoryWebClient.SearchAsync(
                query: query,
                filter: filter,
                limit: 3,
                cancellationToken: cancellationToken);
        }
        return result;
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _memoryWebClient.SearchAsync(query);
        return result;
    }

    public async Task<string> ImportWebPageAsync(string url, bool isPublicAccess, Guid agentId, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Invalid URL provided.", nameof(url));
        }
        var tags = new TagCollection();
        tags["access"] = isPublicAccess ? ["public"] : ["private"];
        var documentId = await _memoryWebClient.ImportWebPageAsync(url, tags: tags, cancellationToken: cancellationToken);
        return documentId;
    }
}
