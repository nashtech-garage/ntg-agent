using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly MemoryWebClient _memoryWebClient;
    private readonly ILogger<KernelMemoryKnowledge> _logger;

    public KernelMemoryKnowledge(IConfiguration configuration, ILogger<KernelMemoryKnowledge> logger)
    {
        var endpoint = Environment.GetEnvironmentVariable($"services__ntg-agent-knowledge__https__0") ?? Environment.GetEnvironmentVariable($"services__ntg-agent-knowledge__http__0") ?? throw new InvalidOperationException("KernelMemory Endpoint configuration is required");
        var apiKey = configuration["KernelMemory:ApiKey"] ?? throw new InvalidOperationException("KernelMemory:ApiKey configuration is required");

        _memoryWebClient = new MemoryWebClient(endpoint, apiKey);
        _logger = logger;
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
        SearchResult result;
        if (tags.Count != 0)
        {
            var filters = (from tagValue in tags
                           select MemoryFilters.ByTag("tags", tagValue)).ToList();
            result = await _memoryWebClient.SearchAsync(
                query: query,
                filters: filters,
                limit: 3,
                cancellationToken: cancellationToken);
        }
        else
        {
            result = await _memoryWebClient.SearchAsync(
                query: query,
                limit: 3,
                cancellationToken: cancellationToken);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("KernelMemoryKnowledge.SearchAsync: {query}, tags:{tags} => {result}", query, string.Join(", ", tags), result.ToJson());
        }
        return result;
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _memoryWebClient.SearchAsync(query);
        return result;
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
}
