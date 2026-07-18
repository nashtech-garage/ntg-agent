using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// Builds the provider-specific <see cref="IChatClient"/> for one agent configuration.
/// </summary>
/// <remarks>
/// This is the provider axis of the agent factory. Each provider family (OpenAI-compatible,
/// Anthropic, …) has one implementation, registered in DI keyed by the agent's
/// <c>ProviderName</c>. Resolving the key replaces the hand-written provider <c>switch</c> that
/// previously lived — duplicated — in both the basic and full agent-creation paths, so adding a
/// provider is "add a class + one keyed registration" rather than editing two switches.
///
/// The returned client already has the shared middleware pipeline (function invocation +
/// OpenTelemetry) and, when reasoning is enabled, the correct reasoning surface applied; the
/// caller only layers on tools and wraps it in an agent.
/// </remarks>
public interface IAgentClientFactory
{
    /// <summary>
    /// Creates a configured chat client for <paramref name="agent"/>.
    /// </summary>
    /// <param name="agent">The agent configuration (provider, model, endpoint, credentials, mode).</param>
    /// <param name="enableThinking">
    /// When <c>true</c>, the provider's reasoning surface is applied for reasoning-capable models
    /// (resolved centrally — see <see cref="ReasoningSurfaceResolver"/>). When <c>false</c>, plain
    /// chat completions are used regardless of the agent's mode; the basic utility agent passes
    /// <c>false</c> so it never incurs reasoning cost.
    /// </param>
    /// <returns>An <see cref="IChatClient"/> with the shared middleware pipeline applied.</returns>
    IChatClient CreateChatClient(Models.Agents.Agent agent, bool enableThinking);
}
