using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly IKernelMemory _kernelMemory;
    private readonly ILogger<KernelMemoryKnowledge> _logger;

    public KernelMemoryKnowledge(IKernelMemory kernelMemory, ILogger<KernelMemoryKnowledge> logger)
    {
        _kernelMemory = kernelMemory ?? throw new ArgumentNullException(nameof(kernelMemory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public async Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        var tagCollection = new TagCollection
        {
            { "agentId", agentId.ToString() },
            { "tags", tags.Cast<string?>().ToList() }
        };
        return await _kernelMemory.ImportDocumentAsync(content, fileName, tags: tagCollection, cancellationToken: cancellationToken);
    }

    public async Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default)
    {
        await _kernelMemory.DeleteDocumentAsync(documentId, cancellationToken: cancellationToken);
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        SearchResult result;
        if (tags.Count != 0)
        {
            var filters = (from tagValue in tags
                           select MemoryFilters.ByTag("tags", tagValue)).ToList();
            result = await _kernelMemory.SearchAsync(
                query: query,
                filters: filters,
                limit: 3,
                cancellationToken: cancellationToken);
        }
        else
        {
            result = await _kernelMemory.SearchAsync(
                query: query,
                limit: 3,
                cancellationToken: cancellationToken);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("KernelMemoryKnowledge.SearchAsync: {Query}, tags:{Tags} => {Result}", query, string.Join(", ", tags), result.ToJson());
        }
        return result;
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _kernelMemory.SearchAsync(query, cancellationToken: cancellationToken);
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
        var documentId = await _kernelMemory.ImportWebPageAsync(url, tags: tagCollection, cancellationToken: cancellationToken);
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
        return await _kernelMemory.ImportDocumentAsync(stream, fileName, tags: tagCollection, cancellationToken: cancellationToken);
    }

    public async Task<StreamableFileContent> ExportDocumentAsync(string documentId, string fileName, Guid agentId, CancellationToken cancellationToken = default)
    {
        return await _kernelMemory.ExportFileAsync(documentId, fileName, cancellationToken: cancellationToken);
    }

    public async Task UpdateDocumentTagsAsync(string documentId, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        // Kernel Memory doesn't support updating tags directly, so we need to delete and re-import
        // First, export the document to get its content
        var fileContent = await _kernelMemory.ExportFileAsync(documentId, fileName, cancellationToken: cancellationToken);
        var stream = await fileContent.GetStreamAsync();

        // Delete the old document
        await _kernelMemory.DeleteDocumentAsync(documentId, cancellationToken: cancellationToken);

        // Re-import with new tags
        var tagCollection = new TagCollection
        {
            { "agentId", agentId.ToString() },
            { "tags", tags.Cast<string?>().ToList() }
        };
        
        // Use the same documentId to maintain consistency
        await _kernelMemory.ImportDocumentAsync(stream, documentId: documentId, fileName: fileName, tags: tagCollection, cancellationToken: cancellationToken);
    }
}
