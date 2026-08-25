using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Services;

public class ModelDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelDiscoveryService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<ModelItem>> GetModelsAsync(
        ProviderType type,
        string? endpoint,
        string? apiKey,
        string? azureAiAccountName = null,
        string? azureAiProjectName = null)
    {
        var client = _httpClientFactory.CreateClient("ModelDiscovery");
        client.Timeout = TimeSpan.FromSeconds(15);

        return type switch
        {
            ProviderType.OpenAI => await GetOpenAIModelsAsync(client, apiKey),
            ProviderType.AzureOpenAI => await GetAzureOpenAIModelsAsync(client, azureAiAccountName, azureAiProjectName, apiKey),
            ProviderType.Anthropic => await GetAnthropicModelsAsync(client, apiKey),
            ProviderType.GoogleGemini => await GetGeminiModelsAsync(client, endpoint, apiKey),
            ProviderType.OpenAICompatible => await GetOpenAICompatModelsAsync(client, endpoint, apiKey),
            _ => throw new NotSupportedException($"Provider type '{type}' is not supported.")
        };
    }

    private static async Task<List<ModelItem>> GetOpenAIModelsAsync(HttpClient client, string? apiKey)
    {
        EnsureApiKey(apiKey, "OpenAI");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OpenAIListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id, SupportsThinking = ThinkingCapableModels.Supports(ProviderType.OpenAI, m.Id) }).ToList() ?? [];
    }

    /// <summary>
    /// Lists an Azure AI Foundry project's <em>deployments</em> (the models actually provisioned
    /// for the account) instead of the full Azure OpenAI catalog. The deployment name is what the
    /// Azure SDK accepts as the model id, so it is used as <see cref="ModelItem.Id"/>; the
    /// underlying model name is surfaced as the display name. Only chat-completion-capable
    /// deployments are returned (embedding-only deployments can't drive agents).
    /// </summary>
    private static async Task<List<ModelItem>> GetAzureOpenAIModelsAsync(
        HttpClient client,
        string? azureAiAccountName,
        string? azureAiProjectName,
        string? apiKey)
    {
        var account = azureAiAccountName?.Trim();
        var project = azureAiProjectName?.Trim();
        if (string.IsNullOrEmpty(account))
            throw new ArgumentException("Azure AI Services account name is required to fetch deployed models.");
        if (string.IsNullOrEmpty(project))
            throw new ArgumentException("Azure AI Foundry project name is required to fetch deployed models.");
        EnsureApiKey(apiKey, "Azure OpenAI");

        var url = $"https://{account}.services.ai.azure.com/api/projects/{project}/deployments?api-version=v1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("api-key", apiKey);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AzureDeploymentListResponse>();
        return result?.Value
            .Where(d => IsChatCompletionCapable(d))
            .Select(d => new ModelItem
            {
                // The deployment name is the identifier the Azure OpenAI SDK expects.
                Id = d.Name,
                DisplayName = string.Equals(d.Name, d.ModelName, StringComparison.OrdinalIgnoreCase) ? null : d.ModelName,
                SupportsThinking = ThinkingCapableModels.Supports(ProviderType.AzureOpenAI, d.Name)
                                   || ThinkingCapableModels.Supports(ProviderType.AzureOpenAI, d.ModelName)
            })
            .ToList() ?? [];
    }

    // The capabilities payload uses snake_case keys ("chat_completion": "true"), which
    // System.Text.Json's case-insensitive matching cannot bind to a PascalCase property
    // (underscores are not folded). A dictionary keeps the raw keys so we can check the
    // value directly, tolerating both string and boolean forms.
    private static bool IsChatCompletionCapable(AzureDeploymentData deployment)
    {
        if (deployment.Capabilities == null
            || !deployment.Capabilities.TryGetValue("chat_completion", out var value))
        {
            return false;
        }
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static async Task<List<ModelItem>> GetAnthropicModelsAsync(HttpClient client, string? apiKey)
    {
        EnsureApiKey(apiKey, "Anthropic");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AnthropicListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id, DisplayName = m.DisplayName, SupportsThinking = ThinkingCapableModels.Supports(ProviderType.Anthropic, m.Id) }).ToList() ?? [];
    }

    private static async Task<List<ModelItem>> GetGeminiModelsAsync(HttpClient client, string? endpoint, string? apiKey)
    {
        EnsureApiKey(apiKey, "Google Gemini");
        var baseUrl = endpoint?.TrimEnd('/') ?? "https://generativelanguage.googleapis.com/v1beta";
        var url = $"{baseUrl}/openai/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OpenAIListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id, SupportsThinking = ThinkingCapableModels.Supports(ProviderType.GoogleGemini, m.Id) }).ToList() ?? [];
    }

    private static async Task<List<ModelItem>> GetOpenAICompatModelsAsync(HttpClient client, string? endpoint, string? apiKey)
    {
        var baseUrl = endpoint?.TrimEnd('/') ?? throw new ArgumentException("Endpoint is required for OpenAI Compatible providers.");
        // If the endpoint already ends with /v1, just append /models; otherwise append /v1/models
        var url = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}/models"
            : $"{baseUrl}/v1/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OpenAIListResponse>();
        return result?.Data.Select(m => new ModelItem { Id = m.Id, SupportsThinking = ThinkingCapableModels.Supports(ProviderType.OpenAICompatible, m.Id) }).ToList() ?? [];
    }

    private static void EnsureApiKey(string? apiKey, string providerName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException($"API key is required for {providerName}. Add it on the provider before fetching models.");
    }

    private class OpenAIListResponse { public List<OpenAIModelData> Data { get; set; } = []; }
    private class OpenAIModelData { public string Id { get; set; } = string.Empty; }
    private class AnthropicListResponse { public List<AnthropicModelData> Data { get; set; } = []; }
    private class AnthropicModelData { public string Id { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; }

    private class AzureDeploymentListResponse { public List<AzureDeploymentData> Value { get; set; } = []; }
    private class AzureDeploymentData
    {
        public string Name { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public Dictionary<string, System.Text.Json.JsonElement>? Capabilities { get; set; }
    }
}
