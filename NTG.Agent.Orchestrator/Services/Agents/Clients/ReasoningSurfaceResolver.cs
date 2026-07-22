namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// How a reasoning-capable model exposes its chain-of-thought over an OpenAI-compatible endpoint.
/// The surface is a property of the model, not the provider hosting it.
/// </summary>
public enum ReasoningSurface
{
    /// <summary>Plain chat completions, no reasoning parameters.</summary>
    None,

    /// <summary>Responses API (<c>/v1/responses</c>) with <c>reasoning.effort</c> — gpt-5.x / o-series.</summary>
    ResponsesApi,

    /// <summary>Chat Completions with a top-level <c>reasoning_effort</c> — DeepSeek family.</summary>
    ChatCompletionsEffort,
}

/// <summary>
/// Resolves the <see cref="ReasoningSurface"/> for a reasoning-enabled agent from its model name
/// (the agent configuration carries no capability flag).
/// </summary>
public static class ReasoningSurfaceResolver
{
    /// <summary>
    /// DeepSeek-family models reject the Responses API <c>reasoning.effort</c> with HTTP 400 and
    /// take a top-level <c>reasoning_effort</c> on Chat Completions instead; everything else uses
    /// the Responses API.
    /// </summary>
    public static ReasoningSurface Resolve(Models.Agents.Agent agent) =>
        agent.ProviderModelName.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            ? ReasoningSurface.ChatCompletionsEffort
            : ReasoningSurface.ResponsesApi;
}
