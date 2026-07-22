using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// Chat-client factory for Anthropic (Claude) models. Separate from
/// <see cref="OpenAICompatibleClientFactory"/> because Anthropic uses its own wire protocol and
/// configures reasoning ("extended thinking") via <see cref="MessageCreateParams"/>.
/// </summary>
public sealed class AnthropicClientFactory : IAgentClientFactory
{
    private const int MaxTokens = 4096;

    // Must be ≥1024 and less than MaxTokens.
    private const int ThinkingTokens = 2048;

    /// <inheritdoc />
    public IChatClient CreateChatClient(Models.Agents.Agent agent, bool enableThinking)
    {
        var chatClient = new AnthropicClient(new ClientOptions { ApiKey = agent.ProviderApiKey })
            .AsIChatClient(defaultModelId: agent.ProviderModelName);

        if (!enableThinking || agent.Mode != AgentMode.Thinking)
        {
            return chatClient.BuildStandard();
        }

        // RawRepresentationFactory is the only supported way to pass the Thinking configuration
        // through the Anthropic Microsoft.Extensions.AI adapter.
        return chatClient.BuildStandard(o => o.RawRepresentationFactory = _ => new MessageCreateParams
        {
            Model = agent.ProviderModelName,
            MaxTokens = o.MaxOutputTokens ?? MaxTokens,
            Messages = [],
            Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(budgetTokens: ThinkingTokens))
        });
    }
}
