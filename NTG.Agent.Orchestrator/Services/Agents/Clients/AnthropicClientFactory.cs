using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// Chat-client factory for Anthropic (Claude) models via the official Anthropic SDK.
/// </summary>
/// <remarks>
/// Anthropic is its own provider family: it does not use the OpenAI wire protocol, and its
/// reasoning ("extended thinking") is configured through the Anthropic-specific
/// <see cref="MessageCreateParams"/> rather than an OpenAI reasoning surface — which is why it has
/// a dedicated factory instead of sharing <see cref="OpenAICompatibleClientFactory"/>.
/// </remarks>
public sealed class AnthropicClientFactory : IAgentClientFactory
{
    /// <summary>Default response cap when the agent does not specify one; thinking budget must be below it.</summary>
    private const int MaxTokens = 4096;

    /// <summary>Reasoning-token budget for extended thinking (must be ≥1024 and less than <see cref="MaxTokens"/>).</summary>
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

        // Extended thinking surfaces chain-of-thought as ThinkingContent items in the streaming
        // response. Requires a compatible Claude model (e.g. claude-3-7-sonnet or later). The
        // Anthropic MEA adapter reads RawRepresentationFactory to build the raw MessageCreateParams,
        // which is the only supported way to pass the Thinking configuration.
        // See: https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/02-agents/AgentWithAnthropic/Agent_Anthropic_Step02_Reasoning/Program.cs
        return chatClient.BuildStandard(o => o.RawRepresentationFactory = _ => new MessageCreateParams
        {
            Model = agent.ProviderModelName,
            MaxTokens = o.MaxOutputTokens ?? MaxTokens,
            Messages = [],
            Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(budgetTokens: ThinkingTokens))
        });
    }
}
