namespace NTG.Agent.Common.Dtos.Agents;

public static class ProviderTypeExtensions
{
    public static string ToDisplayName(this ProviderType type) => type switch
    {
        ProviderType.OpenAI => "OpenAI",
        ProviderType.AzureOpenAI => "Azure Foundry",
        ProviderType.Anthropic => "Anthropic",
        ProviderType.GoogleGemini => "Google Gemini",
        ProviderType.OpenAICompatible => "OpenAI Compatible",
        ProviderType.Custom => "Custom",
        _ => type.ToString()
    };
}
