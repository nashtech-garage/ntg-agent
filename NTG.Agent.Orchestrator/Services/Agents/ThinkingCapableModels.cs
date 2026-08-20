using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Curated, backend-owned list of popular models known to support extended thinking /
/// reasoning mode, matched by case-insensitive substring against the model id. Mirrors
/// the substring-matching idiom previously used by ReasoningSurfaceResolver.
/// </summary>
public static class ThinkingCapableModels
{
    private static readonly Dictionary<ProviderType, string[]> KnownPatterns = new()
    {
        [ProviderType.OpenAI] = ["o1", "o3", "o4-mini", "gpt-5"],
        [ProviderType.AzureOpenAI] = ["o1", "o3", "o4-mini", "gpt-5", "grok-4", "deepseek-v4"],
        [ProviderType.Anthropic] = ["claude-3-7-sonnet", "claude-sonnet-4", "claude-opus-4", "claude-haiku-4-5"],
        [ProviderType.GoogleGemini] = ["gemini-2.0-flash-thinking", "gemini-2.5"],
        [ProviderType.OpenAICompatible] = ["o1", "o3", "gpt-5", "deepseek-v4", "deepseek-r1", "deepseek-reasoner", "qwq", "grok-3", "grok-4"],
        [ProviderType.Custom] = ["o1", "o3", "gpt-5", "deepseek-v4", "deepseek-r1", "deepseek-reasoner", "qwq", "grok-3", "grok-4"],
    };

    public static bool Supports(ProviderType providerType, string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId)
        && KnownPatterns.TryGetValue(providerType, out var patterns)
        && patterns.Any(p => modelId.Contains(p, StringComparison.OrdinalIgnoreCase));
}
