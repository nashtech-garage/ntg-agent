namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Normalizes an Azure OpenAI provider endpoint to the GA v1 inference surface
/// (https://{resource}.openai.azure.com/openai/v1 or https://{account}.services.ai.azure.com/openai/v1).
/// Admins often paste the Foundry "Target URI" that already includes the /openai/v1 suffix;
/// AzureOpenAIClient would append its legacy /openai/... routes onto that and 404, so agents
/// build their clients on plain OpenAIClient against the normalized v1 base instead.
/// </summary>
public static class AzureOpenAIEndpoint
{
    public static Uri ToV1(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint is required for Azure OpenAI providers.", nameof(endpoint));
        }
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)
            ? new Uri(trimmed)
            : new Uri($"{trimmed}/openai/v1");
    }
}
