using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System.ClientModel;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// Chat-client factory for every provider reachable through an OpenAI-compatible v1 surface:
/// standard OpenAI, GitHub Models, Google Gemini, and Azure OpenAI. Azure is handled here — not
/// via <c>AzureOpenAIClient</c> — because its <c>/openai/v1</c> surface accepts the plain client
/// with the key as a Bearer token, while <c>AzureOpenAIClient</c> only targets the legacy
/// api-version deployment API.
/// </summary>
public sealed class OpenAICompatibleClientFactory : IAgentClientFactory
{
    /// <inheritdoc />
    public IChatClient CreateChatClient(Models.Agents.Agent agent, bool enableThinking)
    {
        var client = CreateOpenAIClient(agent);
        var surface = enableThinking ? ReasoningSurfaceResolver.Resolve(agent) : ReasoningSurface.None;

        // Both reasoning surfaces use OpenAI experimental APIs gated behind OPENAI001.
#pragma warning disable OPENAI001
        switch (surface)
        {
            case ReasoningSurface.ResponsesApi:
                return client.GetResponsesClient()
                    .AsIChatClient(agent.ProviderModelName)
                    .BuildStandard(o => o.RawRepresentationFactory = _ => new CreateResponseOptions
                    {
                        ReasoningOptions = new ResponseReasoningOptions
                        {
                            ReasoningEffortLevel = ResponseReasoningEffortLevel.High,
                            ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Detailed,
                        }
                    });

            case ReasoningSurface.ChatCompletionsEffort:
                // The chain-of-thought returns as reasoning_content, which Microsoft.Extensions.AI
                // maps to the same TextReasoningContent type the Responses path emits.
                return client.GetChatClient(agent.ProviderModelName)
                    .AsIChatClient()
                    .BuildStandard(o => o.RawRepresentationFactory = _ => new ChatCompletionOptions
                    {
                        ReasoningEffortLevel = ChatReasoningEffortLevel.High,
                    });

            default:
                return client.GetChatClient(agent.ProviderModelName)
                    .AsIChatClient()
                    .BuildStandard();
        }
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// Builds the underlying <see cref="OpenAIClient"/>, applying the agent's custom endpoint when
    /// supplied (required for GitHub Models / Gemini / Azure, optional for OpenAI).
    /// </summary>
    private static OpenAIClient CreateOpenAIClient(Models.Agents.Agent agent)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(agent.ProviderEndpoint))
        {
            options.Endpoint = new Uri(agent.ProviderEndpoint);
        }

        return new OpenAIClient(new ApiKeyCredential(agent.ProviderApiKey), options);
    }
}
