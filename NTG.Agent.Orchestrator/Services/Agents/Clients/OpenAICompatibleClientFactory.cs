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
                    .BuildStandard(o => o.RawRepresentationFactory = _ => new CreateResponseOptions
                    {
                        ReasoningOptions = new ResponseReasoningOptions
                        {
                            ReasoningEffortLevel = ResponseReasoningEffortLevel.High,
                            ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Detailed,
                        }
                    });

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
