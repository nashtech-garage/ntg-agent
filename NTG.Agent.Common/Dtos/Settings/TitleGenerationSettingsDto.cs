namespace NTG.Agent.Common.Dtos.Settings;

/// <summary>
/// Admin-editable configuration for the conversation-title generation model. Mirrors the provider
/// fields on an agent, but applies system-wide to the lightweight model that names conversations.
/// </summary>
public class TitleGenerationSettingsDto
{
    /// <summary>Provider key (e.g. "AzureOpenAI", "OpenAI", "GitHubModel", "GoogleGemini", "Anthropic").</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Deployment/model name (e.g. "gpt-5.4-mini").</summary>
    public string ProviderModelName { get; set; } = string.Empty;

    /// <summary>Provider endpoint URL (may be empty for providers that use their default).</summary>
    public string ProviderEndpoint { get; set; } = string.Empty;

    /// <summary>API key/token for the provider.</summary>
    public string ProviderApiKey { get; set; } = string.Empty;
}
