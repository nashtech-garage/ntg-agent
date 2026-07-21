using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Services;

public class ModelDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelDiscoveryService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<ModelItem>> GetModelsAsync(ProviderType type, string? endpoint, string? apiKey)
    {
        var client = _httpClientFactory.CreateClient("ModelDiscovery");
        client.Timeout = TimeSpan.FromSeconds(15);

        return type switch
        {
            ProviderType.OpenAI => await GetOpenAIModelsAsync(client, apiKey),
            ProviderType.AzureOpenAI => await GetAzureOpenAIModelsAsync(client, endpoint, apiKey),
            ProviderType.Anthropic => await GetAnthropicModelsAsync(client, apiKey),
            ProviderType.GoogleGemini => await GetGeminiModelsAsync(client, endpoint, apiKey),
            ProviderType.OpenAICompatible => await GetOpenAICompatModelsAsync(client, endpoint, apiKey),
            _ => throw new NotSupportedException($"Provider type '{type}' is not supported.")
        };
    }

    private static async Task<List<ModelItem>> GetOpenAIModelsAsync(HttpClient client, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OpenAIListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id }).ToList() ?? [];
    }

    private static async Task<List<ModelItem>> GetAzureOpenAIModelsAsync(HttpClient client, string? endpoint, string? apiKey)
    {
        var baseUrl = endpoint?.TrimEnd('/') ?? throw new ArgumentException("Endpoint is required for Azure OpenAI.");
        // Strip any trailing /openai/v1 or /openai suffix so we can construct the correct discovery URL
        if (baseUrl.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"/v1".Length];
        else if (baseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl;
        else
            baseUrl = $"{baseUrl}/openai";
        var url = $"{baseUrl}/models?api-version=2024-10-21";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("api-key", apiKey);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OpenAIListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id }).ToList() ?? [];
    }

    private static async Task<List<ModelItem>> GetAnthropicModelsAsync(HttpClient client, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AnthropicListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id, DisplayName = m.DisplayName }).ToList() ?? [];
    }

    private static async Task<List<ModelItem>> GetGeminiModelsAsync(HttpClient client, string? endpoint, string? apiKey)
    {
        var baseUrl = endpoint?.TrimEnd('/') ?? "https://generativelanguage.googleapis.com/v1beta";
        var url = $"{baseUrl}/openai/models";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OpenAIListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id }).ToList() ?? [];
    }

    private static async Task<List<ModelItem>> GetOpenAICompatModelsAsync(HttpClient client, string? endpoint, string? apiKey)
    {
        var baseUrl = endpoint?.TrimEnd('/') ?? throw new ArgumentException("Endpoint is required for OpenAI Compatible providers.");
        // If the endpoint already ends with /v1, just append /models; otherwise append /v1/models
        var url = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}/models"
            : $"{baseUrl}/v1/models";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OpenAIListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id }).ToList() ?? [];
    }

    private class OpenAIListResponse { public List<OpenAIModelData> Data { get; set; } = []; }
    private class OpenAIModelData { public string Id { get; set; } = string.Empty; }
    private class AnthropicListResponse { public List<AnthropicModelData> Data { get; set; } = []; }
    private class AnthropicModelData { public string Id { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; }
}
