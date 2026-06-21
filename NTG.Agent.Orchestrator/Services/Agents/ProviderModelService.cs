using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Exceptions;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Lists models / tests credentials for each supported provider. The provider
/// branches mirror <see cref="AgentFactory"/>'s switch. Every listing call uses
/// the provider's "list models" endpoint, which is the cheapest non-billable probe.
/// </summary>
public class ProviderModelService : IProviderModelService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProviderModelService> _logger;

    // Known providers — must match AgentFactory's switch. Probing anything else is rejected.
    private static readonly HashSet<string> KnownProviders = new(StringComparer.Ordinal)
    {
        "OpenAI", "GitHubModel", "GoogleGemini", "Anthropic", "AzureOpenAI"
    };

    // Endpoint fallbacks used when the admin leaves the endpoint blank.
    private const string OpenAiDefaultEndpoint = "https://api.openai.com/v1";
    private const string GoogleGeminiDefaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai";
    private const string AnthropicDefaultEndpoint = "https://api.anthropic.com";

    // GitHub Models serves its catalog (not the inference endpoint) at a fixed URL.
    private const string GitHubModelsCatalogUrl = "https://models.github.ai/catalog/models";
    private const string AnthropicVersion = "2023-06-01";
    private const string AzureDeploymentsApiVersion = "2024-08-01-preview";

    public ProviderModelService(HttpClient httpClient, ILogger<ProviderModelService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsKnownProvider(string providerName) => KnownProviders.Contains(providerName);

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderProbeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await FetchModelsAsync(request, cancellationToken);
            var message = models.Count > 0
                ? $"Connection successful — {models.Count} model(s) available."
                : "Connection successful.";
            return new ProviderTestResult(true, message);
        }
        catch (ProviderProbeException ex)
        {
            return new ProviderTestResult(false, ex.Message);
        }
    }

    public async Task<List<string>> FetchModelsAsync(ProviderProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsKnownProvider(request.ProviderName))
        {
            throw new ProviderProbeException($"Unknown provider '{request.ProviderName}'.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new ProviderProbeException("An API key is required.");
        }

        using var httpRequest = BuildRequest(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // Never log the API key; only the provider name and the exception type/message.
            _logger.LogWarning(ex, "Provider probe could not reach endpoint for {Provider}", request.ProviderName);
            throw new ProviderProbeException("Could not reach the provider endpoint.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderProbeException("Authentication failed — check the API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Provider probe for {Provider} returned status {Status}",
                    request.ProviderName, (int)response.StatusCode);
                throw new ProviderProbeException($"Provider returned an error (status {(int)response.StatusCode}).");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseModelIds(json);
        }
    }

    private HttpRequestMessage BuildRequest(ProviderProbeRequest request) => request.ProviderName switch
    {
        "GitHubModel" => BuildGitHubCatalogRequest(request.ApiKey),
        "Anthropic" => BuildAnthropicRequest(request.Endpoint, request.ApiKey),
        "AzureOpenAI" => BuildAzureRequest(request.Endpoint, request.ApiKey),
        _ => BuildOpenAiCompatibleRequest(request.ProviderName, request.Endpoint, request.ApiKey),
    };

    // OpenAI and Google Gemini both expose an OpenAI-compatible GET {endpoint}/models with Bearer auth.
    private static HttpRequestMessage BuildOpenAiCompatibleRequest(string provider, string? endpoint, string apiKey)
    {
        var baseUrl = NormalizeEndpoint(endpoint) ?? provider switch
        {
            "GoogleGemini" => GoogleGeminiDefaultEndpoint,
            _ => OpenAiDefaultEndpoint,
        };
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return httpRequest;
    }

    private static HttpRequestMessage BuildGitHubCatalogRequest(string apiKey)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, GitHubModelsCatalogUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return httpRequest;
    }

    // Anthropic: GET {endpoint}/v1/models with x-api-key + anthropic-version headers.
    private static HttpRequestMessage BuildAnthropicRequest(string? endpoint, string apiKey)
    {
        var baseUrl = NormalizeEndpoint(endpoint) ?? AnthropicDefaultEndpoint;
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
        return httpRequest;
    }

    // Azure lists deployments (deployment name == model name) via the data-plane API with api-key header.
    private static HttpRequestMessage BuildAzureRequest(string? endpoint, string apiKey)
    {
        var baseUrl = NormalizeEndpoint(endpoint)
            ?? throw new ProviderProbeException("Azure OpenAI requires a provider endpoint.");
        var url = $"{baseUrl}/openai/deployments?api-version={AzureDeploymentsApiVersion}";
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Add("api-key", apiKey);
        return httpRequest;
    }

    /// <summary>Trims and strips a trailing slash; returns null for blank input.</summary>
    private static string? NormalizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }
        return endpoint.Trim().TrimEnd('/');
    }

    /// <summary>
    /// Extracts model IDs from the various provider response shapes. Handles a root
    /// object with a "data" or "value" array, or a bare array. Each element's "id"
    /// (falling back to "name") is taken. Results are deduplicated and sorted.
    /// </summary>
    private static List<string> ParseModelIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 (root.TryGetProperty("data", out array) || root.TryGetProperty("value", out array)) &&
                 array.ValueKind == JsonValueKind.Array)
        {
            // array assigned by TryGetProperty above.
        }
        else
        {
            return [];
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? id = null;
            if (item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
            {
                id = idElement.GetString();
            }
            else if (item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                id = nameElement.GetString();
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        var result = ids.ToList();
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
