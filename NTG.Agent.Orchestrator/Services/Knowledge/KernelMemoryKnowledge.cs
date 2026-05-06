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
        var filters = ComposeFilters(agentId, tags);
        var result = await _kernelMemory.SearchAsync(
            query: query,
            filters: filters,
            limit: 3,
            cancellationToken: cancellationToken);

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

    private TagCollection ComposeTags(Guid agentId, IEnumerable<string> tags)
    {
        if (agentId == Guid.Empty)
        {
            _logger.LogWarning("ComposeTags: empty agentId — document stored with no agentId tag in Elasticsearch.");
            return new TagCollection();
        }

        var agentIdValue = agentId.ToString().ToLower(CultureInfo.InvariantCulture);

        if (tags == null)
        {
            _logger.LogWarning("ComposeTags: null tags — document stored with agentId only.");
            return new TagCollection { { TagNameAgentId, agentIdValue } };
        }

        var formattedTags = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLower(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

        if (formattedTags.Count == 0)
        {
            _logger.LogWarning("ComposeTags: tags resolved to empty list — document stored with agentId only.");
            return new TagCollection { { TagNameAgentId, agentIdValue } };
        }

        _logger.LogInformation("ComposeTags: agentId={AgentId}, tags=[{Tags}]", agentId, string.Join(", ", formattedTags));
        return new TagCollection
        {
            { TagNameAgentId, agentIdValue },
            { TagNameTags, formattedTags.Cast<string?>().ToList() }
        };
    }

    private List<MemoryFilter> ComposeFilters(Guid agentId, IEnumerable<string> tags)
    {
        if (agentId == Guid.Empty)
        {
            _logger.LogWarning("ComposeFilters: empty agentId — search will run UNFILTERED across all documents.");
            return new List<MemoryFilter>();
        }

        var agentIdValue = agentId.ToString().ToLower(CultureInfo.InvariantCulture);
        var agentOnlyFilter = new MemoryFilter();
        agentOnlyFilter.Add(TagNameAgentId, agentIdValue);

        if (tags == null)
        {
            _logger.LogWarning("ComposeFilters: null tags — filtering by agentId only.");
            return [agentOnlyFilter];
        }

        var formattedTags = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLower(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

        if (formattedTags.Count == 0)
        {
            _logger.LogWarning("ComposeFilters: tags resolved to empty list — filtering by agentId only.");
            return [agentOnlyFilter];
        }

        _logger.LogInformation("ComposeFilters: agentId={AgentId}, tags=[{Tags}]", agentId, string.Join(", ", formattedTags));
        return formattedTags
            .Select(tag => {
                var memoryFilter = MemoryFilters.ByTag(TagNameTags, tag);
                memoryFilter.Add(TagNameAgentId, agentIdValue);
                return memoryFilter;
            })
            .ToList();
    }
}
