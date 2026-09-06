using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Services.Agents;

public interface IThinkingSupportProbe
{
    /// <summary>
    /// Live-checks whether a provider's model supports thinking/reasoning mode by sending a tiny
    /// test request with the thinking parameter enabled, using the same payload shape as the chat
    /// path's Thinking mode.
    /// </summary>
    Task<ThinkingSupportResult> ProbeAsync(Models.Agents.Provider provider, string modelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Replaces the old curated "popular thinking models" list: instead of guessing from the model id,
/// we ask the provider. The probe reuses <see cref="AgentFactory.CreateThinkingChatClient"/>, so the
/// request format is identical to what the chat client sends when an agent runs in Thinking mode —
/// only the message is small ("Hi"). A success verdict means the exact payload the chat path uses
/// was accepted; any failure (including provider rejections of the thinking parameter) reports
/// not-supported together with the provider's error message.
/// </summary>
public class ThinkingSupportProbe : IThinkingSupportProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<ThinkingSupportResult> ProbeAsync(Models.Agents.Provider provider, string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new ThinkingSupportResult { SupportsThinking = false, Error = "Model id is required." };
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Timeout);
            IChatClient chatClient = AgentFactory.CreateThinkingChatClient(provider, modelId, temperature: null, maxOutputTokens: null);
            _ = await chatClient.GetResponseAsync("Hi", cancellationToken: timeoutCts.Token);
            return new ThinkingSupportResult { SupportsThinking = true };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ThinkingSupportResult { SupportsThinking = false, Error = $"The test request timed out after {Timeout.TotalSeconds:0} seconds." };
        }
        catch (Exception ex)
        {
            return new ThinkingSupportResult { SupportsThinking = false, Error = ex.Message };
        }
    }
}
