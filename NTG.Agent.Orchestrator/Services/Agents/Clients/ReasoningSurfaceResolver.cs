namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

public enum ReasoningSurface
{
    None,
    ResponsesApi,
    ChatCompletionsEffort,
}

public static class ReasoningSurfaceResolver
{
    // DeepSeek models reject the Responses API reasoning.effort (HTTP 400 unsupported_parameter)
    // and take a top-level reasoning_effort on Chat Completions instead.
    public static ReasoningSurface Resolve(Models.Agents.Agent agent) =>
        agent.ProviderModelName.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            ? ReasoningSurface.ChatCompletionsEffort
            : ReasoningSurface.ResponsesApi;
}
