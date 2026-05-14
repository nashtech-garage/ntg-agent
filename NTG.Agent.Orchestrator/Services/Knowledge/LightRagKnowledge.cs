using Microsoft.Extensions.Options;
using NTG.Agent.Common.Dtos.Knowledge;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class LightRagKnowledge : IKnowledgeService
{
    private readonly LightRagClient _client;
    private readonly LightRagFileStore _fileStore;
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagKnowledge> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LightRagKnowledge(
        LightRagClient client,
        LightRagFileStore fileStore,
        IOptions<LightRagSettings> settings,
        ILogger<LightRagKnowledge> logger,
        IHttpClientFactory httpClientFactory)
    {
        _client = client;
        _fileStore = fileStore;
        _settings = settings.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> ImportDocumentAsync(Stream content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        // Buffer stream so it can be read twice: once for LightRAG, once for the file store
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        buffer.Position = 0;
        var docId = await _client.InsertFileAsync(new MemoryStream(buffer.ToArray()), fileName, cancellationToken);

        buffer.Position = 0;
        await _fileStore.SaveAsync(agentId, docId, fileName, buffer, cancellationToken);

        _logger.LogInformation("LightRagKnowledge.ImportDocumentAsync: agentId={AgentId} docId={DocId} file={FileName}", agentId, docId, fileName);
        return docId;
    }

    public async Task<string> ImportWebPageAsync(string url, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Invalid URL provided.", nameof(url));

        // LightRAG v1.4.x has no /v1/documents/url endpoint; download and insert as text
        using var http = _httpClientFactory.CreateClient();
        var pageContent = await http.GetStringAsync(url, cancellationToken);
        var description = $"Web page: {url} [AgentId: {agentId}]";
        var docId = await _client.InsertTextAsync(pageContent, description, cancellationToken);

        _logger.LogInformation("LightRagKnowledge.ImportWebPageAsync: agentId={AgentId} docId={DocId} url={Url}", agentId, docId, url);
        return docId;
    }

    public async Task<string> ImportTextContentAsync(string content, string fileName, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be null or empty.", nameof(content));

        var description = $"{fileName} [AgentId: {agentId}]";
        var docId = await _client.InsertTextAsync(content, description, cancellationToken);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _fileStore.SaveAsync(agentId, docId, fileName, stream, cancellationToken);

        _logger.LogInformation("LightRagKnowledge.ImportTextContentAsync: agentId={AgentId} docId={DocId} file={FileName}", agentId, docId, fileName);
        return docId;
    }

    public async Task RemoveDocumentAsync(string documentId, Guid agentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("LightRagKnowledge.RemoveDocumentAsync: agentId={AgentId} documentId={DocumentId}", agentId, documentId);
        await _client.DeleteDocumentAsync(documentId, cancellationToken);
        _fileStore.FindAndDelete(agentId, documentId);
    }

    public async Task<KnowledgeSearchResponse> SearchAsync(string query, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        // NOTE: LightRAG v1.4.x does not support per-agent namespace filtering.
        // AgentId is included in document descriptions during insertion as a metadata hint.
        // Hybrid mode enables local + global graph search with built-in re-ranking.
        var contextText = await _client.QueryAsync(query, _settings.TopK, "hybrid", true, cancellationToken);
        return new KnowledgeSearchResponse(
            IsEmpty: string.IsNullOrWhiteSpace(contextText),
            Query: query,
            Results: [new KnowledgeSearchMatch("lightrag", contextText, 1.0)]);
    }

    public async Task<KnowledgeSearchResponse> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
        => await SearchAsync(query, agentId, new List<string>(), cancellationToken);

    public Task<KnowledgeFileContent> ExportDocumentAsync(string documentId, string fileName, Guid agentId, CancellationToken cancellationToken = default)
    {
        var result = _fileStore.GetAsync(agentId, documentId, fileName)
            ?? throw new FileNotFoundException($"Document '{documentId}' not found in file store for agent '{agentId}'.");
        return Task.FromResult(result);
    }
}
