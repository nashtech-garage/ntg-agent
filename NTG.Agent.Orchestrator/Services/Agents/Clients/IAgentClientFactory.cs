using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// Builds the provider-specific <see cref="IChatClient"/> for one agent configuration.
/// Implementations are registered in DI keyed by the agent's <c>ProviderName</c>; the returned
/// client already has the shared middleware pipeline applied.
/// </summary>
public interface IAgentClientFactory
{
    /// <summary>
    /// Creates a configured chat client for <paramref name="agent"/>. When
    /// <paramref name="enableThinking"/> is <c>false</c>, plain chat completions are used
    /// regardless of the agent's mode.
    /// </summary>
    IChatClient CreateChatClient(Models.Agents.Agent agent, bool enableThinking);
}
