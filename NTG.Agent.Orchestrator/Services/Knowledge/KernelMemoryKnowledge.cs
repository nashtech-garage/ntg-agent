using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly MemoryWebClient _memoryWebClient;

    public KernelMemoryKnowledge(IConfiguration configuration)
    {
        var endpoint = Environment.GetEnvironmentVariable($"services__ntg-agent-knowledge__https__0") ?? Environment.GetEnvironmentVariable($"services__ntg-agent-knowledge__http__0") ?? throw new InvalidOperationException("KernelMemory Endpoint configuration is required");
        var apiKey = configuration["KernelMemory:ApiKey"] ?? throw new InvalidOperationException("KernelMemory:ApiKey configuration is required");

        _memoryWebClient = new MemoryWebClient(endpoint, apiKey);
    }
    public async Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        var tagCollection = new TagCollection
        {
            { "agentId", agentId.ToString() },
            { "tags", tags.Cast<string?>().ToList() }
        };
        return await _memoryWebClient.ImportDocumentAsync(content, fileName, tags: tagCollection);
    }

    public async Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default)
    {
        await _memoryWebClient.DeleteDocumentAsync(documentId);
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (tags != null && tags.Count > 0)
        {
            var filters = (from tagValue in tags
                           select MemoryFilters.ByTag("tags", tagValue)).ToList();
            return await _memoryWebClient.SearchAsync(
                query: query,
                filters: filters,
                limit: 3,
                cancellationToken: cancellationToken);
        }
        else
        {
            return await _memoryWebClient.SearchAsync(
                query: query,
                limit: 3,
                cancellationToken: cancellationToken);
        }
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _memoryWebClient.SearchAsync(query);
        return result;
    }

    public async Task<SearchResult> SearchPerConversationAsync(string query, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var filter = MemoryFilters.ByTag("conversationId", conversationId.ToString());
        var result = await _memoryWebClient.SearchAsync(query, filter: filter, limit: 3);
        return result;
    }

    public async Task<string> ImportWebPageAsync(
        string url,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Invalid URL provided.", nameof(url));
        }
        var tagCollection = new TagCollection
        {
            { "conversationId", conversationId.ToString() }
        };
        // Use the conversationId as the collection name to keep memory per conversation
        var documentId = await _memoryWebClient.ImportWebPageAsync(
            url,
            tags: tagCollection,
            cancellationToken: cancellationToken
        );

        return documentId;
    }


    public async Task<string> ImportWebPageAsync(string url, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Invalid URL provided.", nameof(url));
        }
        var tagCollection = new TagCollection
        {
            { "agentId", agentId.ToString() },
            { "tags", tags.Cast<string?>().ToList() }
        };
        var documentId = await _memoryWebClient.ImportWebPageAsync(url, tags: tagCollection, cancellationToken: cancellationToken);
        return documentId;
    }

    public async Task<string> ImportTextContentAsync(string content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be null or empty.", nameof(content));
        }

        var tagCollection = new TagCollection
        {
            { "agentId", agentId.ToString() },
            { "tags", tags.Cast<string?>().ToList() }
        };

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return await _memoryWebClient.ImportDocumentAsync(stream, fileName, tags: tagCollection, cancellationToken: cancellationToken);
    }

    public async Task<StreamableFileContent> ExportDocumentAsync(string documentId, string fileName, Guid agentId, CancellationToken cancellationToken = default)
    {
        return await _memoryWebClient.ExportFileAsync(documentId, fileName, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Clears all documents stored for a specific conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID to clear memory for.</param>
    public async Task ClearDocumentsPerConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        // Create a filter for documents tagged with this conversationId
        var filter = MemoryFilters.ByTag("conversationId", conversationId.ToString());

        // Retrieve all matching documents
        var searchResult = await _memoryWebClient.SearchAsync(
            query: "*", // wildcard to match all content
            filter: filter,
            limit: int.MaxValue, // get all documents
            cancellationToken: cancellationToken
        );

        if (searchResult.NoResult)
            return;

        // Delete each document
        foreach (var doc in searchResult.Results)
        {
            try
            {
                await _memoryWebClient.DeleteDocumentAsync(doc.DocumentId, cancellationToken: cancellationToken);
            }
            catch
            {
                // Ignore failures for individual documents
                continue;
            }
        }
    }
}
