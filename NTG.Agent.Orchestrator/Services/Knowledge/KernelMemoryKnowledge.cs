using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DataFormats.WebPages;
using NTG.Agent.Orchestrator.Extentions;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly IKernelMemory _kernelMemory;
    private readonly ILogger<KernelMemoryKnowledge> _logger;
    private readonly IWebScraper _webScraper;

    public KernelMemoryKnowledge(IKernelMemory kernelMemory,
        ILogger<KernelMemoryKnowledge> logger,
        IWebScraper webScraper)
    {
        _kernelMemory = kernelMemory ?? throw new ArgumentNullException(nameof(kernelMemory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webScraper = webScraper;
    }
    public async Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        var tagCollection = new TagCollection
        {
            { "agentId", agentId.ToString() },
            { "tags", tags.Cast<string?>().ToList() }
        };
        return await _kernelMemory.ImportDocumentAsync(content, fileName, tags: tagCollection);
    }

    public async Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default)
    {
        await _kernelMemory.DeleteDocumentAsync(documentId);
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

        result.Results = result.Results
            .Where(r => r.Partitions.Any(p => !p.Tags.ContainsKey("conversationId")))
            .Select(r => { r.Partitions = r.Partitions.Where(p => !p.Tags.ContainsKey("conversationId")).ToList(); return r; })
            .ToList();


        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("KernelMemoryKnowledge.SearchAsync: {query}, tags:{tags} => {result}", query, string.Join(", ", tags), result.ToJson());
        }
        return result;
    }

    public async Task<SearchResult> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _kernelMemory.SearchAsync(query);
        return result;
    }

    public async Task<SearchResult> SearchPerConversationAsync(string query, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var filter = MemoryFilters.ByTag("conversationId", conversationId.ToString());
        var result = await _kernelMemory.SearchAsync(query, filter: filter, limit: 3);
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
            { "conversationId", conversationId.ToString() },
            { "sourceUrl", url }
        };
        // Use the conversationId as the collection name to keep memory per conversation
        string documentId = string.Empty;
        try
        {
            var webPage = await _webScraper.GetContentAsync(url, cancellationToken);
            var htmlContent = webPage.Content.ToString();
            var cleanedHtml = htmlContent.CleanHtml();
            documentId = await _kernelMemory.ImportTextAsync(
                cleanedHtml,
                tags: tagCollection,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot import WebPage cleaned content to kernelMemory");
        }

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

    /// <summary>
    /// Clears all documents stored for a specific conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID to clear memory for.</param>
    public async Task ClearDocumentsPerConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        // Create a filter for documents tagged with this conversationId
        var filter = MemoryFilters.ByTag("conversationId", conversationId.ToString());

        // Retrieve all matching documents
        var searchResult = await _kernelMemory.SearchAsync(
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
                await _kernelMemory.DeleteDocumentAsync(doc.DocumentId, cancellationToken: cancellationToken);
            }
            catch
            {
                // Ignore failures for individual documents
                continue;
            }
        }
    }
}
