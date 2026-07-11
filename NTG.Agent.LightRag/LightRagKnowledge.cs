using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.Common.Dtos.Knowledge;
using NTG.Agent.Common.Knowledge;

namespace NTG.Agent.LightRag;

public class LightRagKnowledge : IKnowledgeService
{
    private readonly LightRagClientFactory _clientFactory;
    private readonly LightRagFileStore _fileStore;
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagKnowledge> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LightRagKnowledge(
        LightRagClientFactory clientFactory,
        LightRagFileStore fileStore,
        IOptions<LightRagSettings> settings,
        ILogger<LightRagKnowledge> logger,
        IHttpClientFactory httpClientFactory)
    {
        _clientFactory = clientFactory;
        _fileStore = fileStore;
        _settings = settings.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> BeginImportDocumentAsync(Stream content, string fileName, Guid agentId, Guid documentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        var client = await _clientFactory.GetClientAsync(agentId, cancellationToken);

        // Buffer stream so it can be read twice: once for LightRAG, once for the file store.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        // Persist the original bytes immediately (keyed by the local Document.Id) so the file is
        // downloadable while LightRAG is still extracting in the background.
        buffer.Position = 0;
        await _fileStore.SaveAsync(agentId, documentId, fileName, buffer, cancellationToken);

        // LightRAG /documents/upload is async — it returns a track_id right away and assigns the
        // real doc-id later. We do NOT wait here; the background worker polls for completion.
        buffer.Position = 0;
        var trackId = await client.InsertFileAsync(new MemoryStream(buffer.ToArray()), fileName, cancellationToken);

        _logger.LogInformation("LightRagKnowledge.BeginImportDocumentAsync: agentId={AgentId} documentId={DocumentId} trackId={TrackId} file={FileName}", agentId, documentId, trackId, fileName);
        return trackId;
    }

    public async Task<string> BeginImportWebPageAsync(string url, Guid agentId, Guid documentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Invalid URL provided.", nameof(url));

        // SSRF guard: the URL is user-supplied and fetched server-side, so refuse hosts that
        // resolve to loopback/private/link-local ranges (cloud metadata, internal services...).
        var addresses = await System.Net.Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || Array.Exists(addresses, IsPrivateOrReservedAddress))
            throw new ArgumentException("URL host resolves to a private or reserved address.", nameof(url));

        var client = await _clientFactory.GetClientAsync(agentId, cancellationToken);

        // LightRAG v1.4.x has no /v1/documents/url endpoint; download and insert as text.
        // The fetch uses the validated absolute Uri, never the raw string.
        using var http = _httpClientFactory.CreateClient();
        var pageContent = await http.GetStringAsync(uri, cancellationToken);
        var description = $"Web page: {url}";
        var trackId = await client.InsertTextAsync(pageContent, description, cancellationToken);

        _logger.LogInformation("LightRagKnowledge.BeginImportWebPageAsync: agentId={AgentId} documentId={DocumentId} trackId={TrackId} url={Url}", agentId, documentId, trackId, url);
        return trackId;
    }

    public async Task<string> BeginImportTextContentAsync(string content, string fileName, Guid agentId, Guid documentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be null or empty.", nameof(content));

        var client = await _clientFactory.GetClientAsync(agentId, cancellationToken);

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
        {
            await _fileStore.SaveAsync(agentId, documentId, fileName, stream, cancellationToken);
        }

        var trackId = await client.InsertTextAsync(content, fileName, cancellationToken);

        _logger.LogInformation("LightRagKnowledge.BeginImportTextContentAsync: agentId={AgentId} documentId={DocumentId} trackId={TrackId} file={FileName}", agentId, documentId, trackId, fileName);
        return trackId;
    }

    public async Task<DocumentIngestResult> CheckIngestStatusAsync(Guid agentId, string trackId, CancellationToken cancellationToken = default)
    {
        var client = await _clientFactory.GetClientAsync(agentId, cancellationToken);
        var status = await client.GetTrackStatusAsync(trackId, cancellationToken);

        if (status.Documents.Count == 0)
            return new DocumentIngestResult(DocumentStatus.Processing, null, null);

        var doc = status.Documents[0];
        switch (doc.Status?.ToUpperInvariant())
        {
            case "PROCESSED":
                return new DocumentIngestResult(DocumentStatus.Completed, doc.Id, null);
            case "FAILED":
                var reason = string.IsNullOrWhiteSpace(doc.ErrorMsg)
                    ? "LightRAG failed to extract this document. Check the LightRAG WebUI for details."
                    : doc.ErrorMsg!;
                _logger.LogWarning("LightRagKnowledge: extraction failed (trackId={TrackId} docId={DocId}): {Reason}", trackId, doc.Id, reason);
                return new DocumentIngestResult(DocumentStatus.Failed, doc.Id, reason);
            default:
                // PENDING / PROCESSING / PREPROCESSED — still in flight.
                return new DocumentIngestResult(DocumentStatus.Processing, null, null);
        }
    }

    public async Task RemoveDocumentAsync(Guid agentId, Guid documentId, string? knowledgeDocId, string? trackId, CancellationToken cancellationToken = default)
    {
        var client = await _clientFactory.GetClientAsync(agentId, cancellationToken);

        // Resolve the LightRAG doc-id: if the document never finished processing we may only have a
        // track_id, so try once to resolve it — this prevents a delete-while-processing from leaving
        // an orphan in LightRAG once extraction completes.
        var effectiveDocId = knowledgeDocId;
        if (string.IsNullOrEmpty(effectiveDocId) && !string.IsNullOrEmpty(trackId))
        {
            try
            {
                var status = await client.GetTrackStatusAsync(trackId, cancellationToken);
                effectiveDocId = status.Documents.Count > 0 ? status.Documents[0].Id : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LightRagKnowledge.RemoveDocumentAsync: could not resolve docId from trackId {TrackId}", trackId);
            }
        }

        if (!string.IsNullOrEmpty(effectiveDocId))
        {
            _logger.LogInformation("LightRagKnowledge.RemoveDocumentAsync: agentId={AgentId} docId={DocId}", agentId, effectiveDocId);
            await client.DeleteDocumentAsync(effectiveDocId, cancellationToken);
        }

        _fileStore.FindAndDelete(agentId, documentId);
    }

    public async Task<KnowledgeSearchResponse> SearchAsync(string query, Guid agentId, List<string> tags, CancellationToken cancellationToken = default)
    {
        // Each agent has its own container/workspace, so this query only sees this
        // agent's documents. Hybrid mode enables local + global graph search with re-ranking.
        //
        // NOTE: LightRAG's /query endpoint does not support tag-based filtering. Tags are
        // accepted for interface compatibility but are not applied. Per-agent workspace
        // isolation already scopes results to a single agent's documents.
        if (tags is { Count: > 0 })
        {
            _logger.LogWarning("LightRagKnowledge.SearchAsync: tag-based filtering is not supported by LightRAG; " +
                "tags were provided but will be ignored for agentId={AgentId}.", agentId);
        }

        var client = await _clientFactory.GetClientAsync(agentId, cancellationToken);
        var contextText = await client.QueryAsync(query, _settings.TopK, "hybrid", true, cancellationToken);
        return new KnowledgeSearchResponse(
            IsEmpty: string.IsNullOrWhiteSpace(contextText),
            Query: query,
            Results: [new KnowledgeSearchMatch("lightrag", contextText, 1.0)]);
    }

    public async Task<KnowledgeSearchResponse> SearchAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
        => await SearchAsync(query, agentId, new List<string>(), cancellationToken);

    public Task<KnowledgeFileContent> ExportDocumentAsync(Guid agentId, Guid documentId, string? knowledgeDocId, string fileName, CancellationToken cancellationToken = default)
    {
        var result = _fileStore.GetAsync(agentId, documentId, fileName)
            ?? throw new FileNotFoundException($"Document '{documentId}' not found in file store for agent '{agentId}'.");
        return Task.FromResult(result);
    }

    // SSRF guard for BeginImportWebPageAsync: true for loopback, private (RFC 1918),
    // link-local (169.254.0.0/16 — includes cloud metadata endpoints), and IPv6
    // unique-local/link-local addresses.
    private static bool IsPrivateOrReservedAddress(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6) return IsPrivateOrReservedAddress(address.MapToIPv4());
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,                             // 10.0.0.0/8
            172 => bytes[1] >= 16 && bytes[1] < 32, // 172.16.0.0/12
            192 => bytes[1] == 168,                 // 192.168.0.0/16
            169 => bytes[1] == 254,                 // 169.254.0.0/16 (link-local / metadata)
            0 => true,                              // 0.0.0.0/8
            _ => false,
        };
    }
}
