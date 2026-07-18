using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System.ClientModel;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// Chat-client factory for every provider reachable through an OpenAI-compatible v1 surface:
/// standard OpenAI, GitHub Models, Google Gemini, and Azure OpenAI.
/// </summary>
/// <remarks>
/// These providers all speak the same wire protocol via <see cref="OpenAIClient"/>; they differ
/// only in whether a custom endpoint is supplied (OpenAI is optional, the others require one).
/// Azure is included here — not routed through <c>AzureOpenAIClient</c> — because its
/// OpenAI-compatible <c>/openai/v1</c> surface accepts the plain client with the key as a Bearer
/// token, whereas <c>AzureOpenAIClient</c> targets the legacy api-version deployment API and
/// cannot address <c>/openai/v1</c>.
///
/// The reasoning surface is chosen centrally by <see cref="ReasoningSurfaceResolver"/>, so a
/// DeepSeek model routes to Chat-Completions reasoning no matter which of these providers hosts it.
/// </remarks>
public sealed class OpenAICompatibleClientFactory : IAgentClientFactory
{
    /// <inheritdoc />
    public IChatClient CreateChatClient(Models.Agents.Agent agent, bool enableThinking)
    {
        var client = CreateOpenAIClient(agent);
        var surface = enableThinking ? ReasoningSurfaceResolver.Resolve(agent) : ReasoningSurface.None;

        // The reasoning surfaces use OpenAI experimental APIs (Responses client, ChatCompletionOptions
        // reasoning effort), both gated behind the OPENAI001 evaluation diagnostic.
#pragma warning disable OPENAI001
        switch (surface)
        {
            case ReasoningSurface.ResponsesApi:
                // gpt-5.x / o-series: Responses API (/v1/responses) with reasoning.effort.
                // See: https://github.com/rwjdk/MicrosoftAgentFrameworkSamples/blob/main/src/OpenAIResponsesApi.ReasoningSummary/Program.cs
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
                // DeepSeek family: Chat Completions with a top-level reasoning_effort. The
                // chain-of-thought returns as reasoning_content, which Microsoft.Extensions.AI maps
                // to TextReasoningContent — the same type the Responses path emits, so the
                // streaming/UI layer is unchanged.
                return client.GetChatClient(agent.ProviderModelName)
                    .AsIChatClient()
                    .BuildStandard(o => o.RawRepresentationFactory = _ => new ChatCompletionOptions
                    {
                        ReasoningEffortLevel = ChatReasoningEffortLevel.High,
                    });

            default:
                // Plain chat completions (/v1/chat/completions).
                return client.GetChatClient(agent.ProviderModelName)
                    .AsIChatClient()
                    .BuildStandard();
        }
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// Builds the underlying <see cref="OpenAIClient"/>, applying a custom endpoint when the agent
    /// configuration supplies one (required for GitHub Models / Gemini / Azure, optional for OpenAI).
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
