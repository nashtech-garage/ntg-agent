using Microsoft.KernelMemory;
using System.Text.RegularExpressions;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly IKernelMemory _kernelMemory;
    private readonly ILogger<KernelMemoryKnowledge> _logger;
    private readonly HttpClient _httpClient;

    public KernelMemoryKnowledge(IKernelMemory kernelMemory,
        ILogger<KernelMemoryKnowledge> logger,
        IHttpClientFactory httpClientFactory)
    {
        _kernelMemory = kernelMemory ?? throw new ArgumentNullException(nameof(kernelMemory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClientFactory.CreateClient();
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
        var result = await _kernelMemory.SearchAsync(query, index:conversationId.ToString(), limit: 3);
        return result;
    }

    public async Task<string> ImportWebPageAsync(
    string sourceUrl,
    Guid conversationId,
    CancellationToken cancellationToken = default)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // per-page timeout
        var documentID = string.Empty;

        try
        {
            _logger.LogInformation($"[INFO] Fetching: {sourceUrl}");

            // 1. Fetch the raw HTML
            var html = await _httpClient.GetStringAsync(sourceUrl, cts.Token);

            // 2. Clean the HTML -> plain text
            var cleanText = CleanHtml(html);

            // 4. Import to memory
            documentID = await _kernelMemory.ImportTextAsync(
                index: conversationId.ToString(), 
                text: cleanText,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation($"[OK] Imported: {sourceUrl}");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"[TIMEOUT] Skipped: {sourceUrl}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] Failed {sourceUrl}: {ex.Message}");
        }

        return documentID;
    }


    public string CleanHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // 1. Remove scripts & styles & noscript
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<noscript[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);

        // 2. Remove input, textarea, select
        html = Regex.Replace(html, @"<(input|textarea|select)[\s\S]*?>", "", RegexOptions.IgnoreCase);

        // 3. Replace <br> and </p> with line breaks for readability
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</p\s*>", "\n", RegexOptions.IgnoreCase);

        // 4. Remove all other HTML tags
        html = Regex.Replace(html, @"<[^>]+>", " ");

        // 5. Decode common entities (basic subset)
        html = System.Net.WebUtility.HtmlDecode(html);

        // 6. Collapse multiple whitespace
        html = Regex.Replace(html, @"\s{2,}", " ").Trim();

        return html;
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
