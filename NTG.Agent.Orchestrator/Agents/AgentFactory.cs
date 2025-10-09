using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Plugins;
using OpenAI;
using System.ClientModel;

namespace NTG.Agent.Orchestrator.Agents;

public class AgentFactory
{
    private readonly IConfiguration _configuration;
    public AgentFactory(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public AIAgent CreateAgent(Guid agentId)
    {
        // TODO Get agent configuration from database by agentId
        string agentProvider = "OpenAI";
        switch (agentProvider)
        {
            case "OpenAI":
                return CreateOpenAIAgent();
            case "AzureOpenAI":
                return CreateAzureOpenAIAgent();
            default:
                throw new NotSupportedException($"Agent provider '{agentProvider}' is not supported.");
        }
    }

    public AIAgent CreateBasicAgent(string instructions)
    {
        // TODO Make it configurable, now only support GitHub model
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_configuration["GitHub:Models:Endpoint"]!)
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(_configuration["GitHub:Models:GitHubToken"]!), clientOptions);
        var agent = openAiClient.GetChatClient(_configuration["GitHub:Models:ModelId"])
            .CreateAIAgent(instructions: instructions);
        return agent;
    }

    private AIAgent CreateOpenAIAgent()
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_configuration["GitHub:Models:Endpoint"]!)
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(_configuration["GitHub:Models:GitHubToken"]!), clientOptions);

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(DateTimePlugin.GetCurrentDateTime)
        };

        var agent = openAiClient.GetChatClient(_configuration["GitHub:Models:ModelId"])
            .CreateAIAgent(instructions: "You are a helpful assistant.", name: "NTG.Agent", tools: tools);
        return agent;
    }

    private AIAgent CreateAzureOpenAIAgent()
    {
        AIAgent agent = new AzureOpenAIClient(
            new Uri(_configuration["Azure:OpenAI:Endpoint"]!),
            new ApiKeyCredential(_configuration["Azure:OpenAI:ApiKey"]!))
             .GetChatClient(_configuration["Azure:OpenAI:DeploymentName"])
             .CreateAIAgent(instructions: "You are a helpful assistant.", name: "NTG.Agent");
        return agent;
    }
}
