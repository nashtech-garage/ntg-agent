namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// How a reasoning-capable model exposes its chain-of-thought over an OpenAI-compatible endpoint.
/// </summary>
/// <remarks>
/// This is the reasoning axis of the agent factory — orthogonal to the provider axis. A model's
/// reasoning surface is a property of the <em>model</em>, not the provider hosting it: the same
/// DeepSeek model needs Chat-Completions reasoning whether it is served by Azure, GitHub Models,
/// or a generic OpenAI endpoint. Keeping the decision here (rather than inside one provider
/// factory) is what lets every OpenAI-compatible provider route reasoning models correctly.
/// </remarks>
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
/// Resolves the <see cref="ReasoningSurface"/> for a reasoning-enabled agent from its model name.
/// </summary>
/// <remarks>
/// Detection is by model name because the agent configuration carries no capability flag. This is
/// the single place that knowledge lives; adding a new reasoning quirk is one entry here and it
/// applies to every OpenAI-compatible provider at once. Callers only invoke this when reasoning is
/// enabled — the <see cref="ReasoningSurface.None"/> case is handled by the caller.
/// </remarks>
public static class ReasoningSurfaceResolver
{
    /// <summary>
    /// Maps a reasoning-enabled agent to the API surface its model accepts.
    /// </summary>
    /// <param name="agent">The agent whose model name is inspected.</param>
    /// <returns>
    /// <see cref="ReasoningSurface.ChatCompletionsEffort"/> for DeepSeek-family models (which reject
    /// the Responses API <c>reasoning.effort</c> with HTTP 400 and instead return their
    /// chain-of-thought as <c>reasoning_content</c>); otherwise <see cref="ReasoningSurface.ResponsesApi"/>.
    /// </returns>
    public static ReasoningSurface Resolve(Models.Agents.Agent agent) =>
        agent.ProviderModelName.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            ? ReasoningSurface.ChatCompletionsEffort
            : ReasoningSurface.ResponsesApi;
}
