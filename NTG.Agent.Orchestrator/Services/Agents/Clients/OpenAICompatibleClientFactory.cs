using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System.ClientModel;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

// Azure OpenAI is served through the plain OpenAIClient on purpose: its /openai/v1 surface takes
// the key as a Bearer token, which AzureOpenAIClient (legacy api-version API) cannot address.
public sealed class OpenAICompatibleClientFactory : IAgentClientFactory
{
    public IChatClient CreateChatClient(Models.Agents.Agent agent, bool enableThinking)
    {
        var client = CreateOpenAIClient(agent);
        var surface = enableThinking ? ReasoningSurfaceResolver.Resolve(agent) : ReasoningSurface.None;

#pragma warning disable OPENAI001
        switch (surface)
        {
            case ReasoningSurface.ResponsesApi:
                return client.GetResponsesClient()
                    .AsIChatClient(agent.ProviderModelName)
                    .BuildStandard(ConfigureResponsesOptions);

            case ReasoningSurface.ChatCompletionsEffort:
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

#pragma warning disable OPENAI001
    // The Responses API is stateful unless told otherwise, and that statefulness is what broke the tool
    // loop. OpenAIResponsesChatClient surfaces the stored response id as ChatResponse.ConversationId;
    // FunctionInvokingChatClient reads a non-null ConversationId as "the service owns the history", throws
    // away everything it has accumulated and sends the next iteration as previous_response_id plus the bare
    // tool result. Azure's /openai/v1 surface does not reliably retain those responses — and does not admit
    // it in the response's own "store" field, which is the only signal the client would honour — so the
    // continuation immediately after a successful tool call dies with
    // HTTP 400 previous_response_not_found and takes the whole run with it. Intermittently, which is worse.
    // store=false keeps ConversationId null, so the full history travels on every request and nothing
    // depends on server state we cannot inspect. Reasoning stays configured here because the summaries are
    // the entire reason this surface is preferred over chat completions.
    public static void ConfigureResponsesOptions(ChatOptions options) =>
        options.RawRepresentationFactory = _ => new CreateResponseOptions
        {
            StoredOutputEnabled = false,
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ResponseReasoningEffortLevel.High,
                // Summary support varies per model; Auto selects the best available.
                ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Auto,
            }
        };
#pragma warning restore OPENAI001

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
