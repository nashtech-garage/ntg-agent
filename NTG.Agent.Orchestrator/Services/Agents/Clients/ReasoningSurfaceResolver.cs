namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

public enum ReasoningSurface
{
    None,
    ResponsesApi,
    ChatCompletionsEffort,
}

public static class ReasoningSurfaceResolver
{
    // GitHub Models and Gemini's OpenAI-compat layer expose no /responses endpoint (verified 404);
    // reasoning goes through chat completions' reasoning_effort there. DeepSeek models reject the
    // Responses API reasoning.effort (HTTP 400 unsupported_parameter) on every provider.
    public static ReasoningSurface Resolve(Models.Agents.Agent agent) =>
        agent.ProviderModelName.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
        || agent.ProviderName.Equals("GitHubModel", StringComparison.OrdinalIgnoreCase)
        || agent.ProviderName.Equals("GoogleGemini", StringComparison.OrdinalIgnoreCase)
            ? ReasoningSurface.ChatCompletionsEffort
            : ReasoningSurface.ResponsesApi;
}
