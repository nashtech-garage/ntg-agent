using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class LightRagClient
{
    private readonly HttpClient _http;
    private readonly ILogger<LightRagClient> _logger;

    public LightRagClient(HttpClient http, ILogger<LightRagClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> InsertTextAsync(string text, string? description = null, CancellationToken ct = default)
    {
        var body = new InsertTextRequest(text, description);
        var response = await _http.PostAsJsonAsync("/v1/documents/text", body, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InsertDocumentResponse>(ct);
        _logger.LogInformation("LightRagClient.InsertTextAsync: docId={DocId}", result!.Id);
        return result.Id;
    }

    public async Task<string> InsertFileAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        form.Add(streamContent, "file", fileName);
        var response = await _http.PostAsync("/v1/documents/file", form, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InsertDocumentResponse>(ct);
        _logger.LogInformation("LightRagClient.InsertFileAsync: file={FileName} docId={DocId}", fileName, result!.Id);
        return result.Id;
    }

    public async Task DeleteDocumentAsync(string docId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/documents")
        {
            Content = JsonContent.Create(new DeleteDocumentRequest(docId))
        };
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("LightRagClient.DeleteDocumentAsync: docId={DocId}", docId);
    }

    public async Task<string> QueryAsync(string query, int topK = 60, string mode = "hybrid", bool onlyNeedContext = true, CancellationToken ct = default)
    {
        var body = new QueryRequest(query, mode, onlyNeedContext, topK, 4000, 4000, 4000);
        var response = await _http.PostAsJsonAsync("/v1/query", body, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<QueryResponse>(ct);
        return result!.Response;
    }

    private sealed record InsertTextRequest(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("description")] string? Description);

    private sealed record InsertDocumentResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string Status);

    private sealed record DeleteDocumentRequest(
        [property: JsonPropertyName("doc_id")] string DocId);

    private sealed record QueryRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("only_need_context")] bool OnlyNeedContext,
        [property: JsonPropertyName("top_k")] int TopK,
        [property: JsonPropertyName("max_token_for_text_unit")] int MaxTokenForTextUnit,
        [property: JsonPropertyName("max_token_for_global_context")] int MaxTokenForGlobalContext,
        [property: JsonPropertyName("max_token_for_local_context")] int MaxTokenForLocalContext);

    private sealed record QueryResponse(
        [property: JsonPropertyName("response")] string Response);
}
