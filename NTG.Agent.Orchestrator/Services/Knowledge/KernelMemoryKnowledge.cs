using Microsoft.KernelMemory;
using System.Globalization;
namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class KernelMemoryKnowledge : IKnowledgeService
{
    private readonly IKernelMemory _kernelMemory;
    private readonly ILogger<KernelMemoryKnowledge> _logger;
    private const string TagNameAgentId = "agentId";
    private const string TagNameTags = "tags";

    public KernelMemoryKnowledge(IKernelMemory kernelMemory, ILogger<KernelMemoryKnowledge> logger)
    {
        _kernelMemory = kernelMemory ?? throw new ArgumentNullException(nameof(kernelMemory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public async Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        var tagCollection = ComposeTags(agentId, tags);
        return await _kernelMemory.ImportDocumentAsync(content, fileName, tags: tagCollection, cancellationToken: cancellationToken);
    }

    public async Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default)
    {
        await _kernelMemory.DeleteDocumentAsync(documentId, cancellationToken: cancellationToken);
    }
    public async Task<SearchResult> SearchAsync(string query, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        SearchResult result;
        var filters = ComposeFilters(agentId, tags);
        if (filters.Count > 0)
        {
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
        var tagCollection = ComposeTags(agentId, tags);
        var documentId = await _kernelMemory.ImportWebPageAsync(url, tags: tagCollection, cancellationToken: cancellationToken);
        return documentId;
    }

    public async Task<string> ImportTextContentAsync(string content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be null or empty.", nameof(content));
        }

        var tagCollection = ComposeTags(agentId, tags);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return await _kernelMemory.ImportDocumentAsync(stream, fileName, tags: tagCollection, cancellationToken: cancellationToken);
    }

    public async Task<StreamableFileContent> ExportDocumentAsync(string documentId, string fileName, Guid agentId, CancellationToken cancellationToken = default)
    {
        return await _kernelMemory.ExportFileAsync(documentId, fileName, cancellationToken: cancellationToken);
    }

    private static TagCollection ComposeTags(Guid agentId, IEnumerable<string> tags)
    {
        if (tags == null || agentId == Guid.Empty)
        {
            return new TagCollection();
        }

        var formattedTags = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLower(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

        if (formattedTags.Count == 0)
        {
            return new TagCollection();
        }

        return new TagCollection
        {
            { TagNameAgentId, agentId.ToString().ToLower(CultureInfo.InvariantCulture) },
            { TagNameTags, formattedTags.Cast<string?>().ToList() }
        };
    }
    private static List<MemoryFilter> ComposeFilters(Guid agentId, IEnumerable<string> tags)
    {
        if (tags == null || agentId == Guid.Empty)
        {
            return new List<MemoryFilter>();
        }
        var formattedTags = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLower(CultureInfo.InvariantCulture))
            .Distinct();

        var filters = formattedTags
               .Select(tag => {
                   var memoryFilter = MemoryFilters.ByTag(TagNameTags, tag);
                   memoryFilter.Add(TagNameAgentId, agentId.ToString().ToLower(CultureInfo.InvariantCulture));
                   return memoryFilter;
               })
               .ToList();
        return filters;
    }
}
