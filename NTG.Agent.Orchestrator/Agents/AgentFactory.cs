using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Plugins;
using OpenAI;
using System.ClientModel;

namespace NTG.Agent.Orchestrator.Agents;

public class AgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly AgentDbContext _agentDbContext;

    public AgentFactory(IConfiguration configuration, AgentDbContext agentDbContext)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
    }

    public AIAgent CreateAgent(Guid agentId)
    {
        var agentConfig = _agentDbContext.Agents.FirstOrDefault(a => a.Id == agentId) ?? throw new ArgumentException($"Agent with ID '{agentId}' not found.");
        string agentProvider = agentConfig.ProviderName;
        return agentProvider switch
        {
            "GitHubModel" => CreateOpenAIAgent(agentConfig),
            "AzureOpenAI" => CreateAzureOpenAIAgent(agentConfig),
            _ => throw new NotSupportedException($"Agent provider '{agentProvider}' is not supported."),
        };
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

    private AIAgent CreateOpenAIAgent(Models.Agents.Agent agent)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(agent.ProviderEndpoint)
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(agent.ProviderApiKey), clientOptions);

        var chatClient = openAiClient.GetChatClient(agent.ProviderModelName)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(DateTimePlugin.GetCurrentDateTime)
        };

        return Create(chatClient, instructions: agent.Instructions, name: "NTG.Agent", tools: tools);
    }

    private AIAgent CreateAzureOpenAIAgent(Models.Agents.Agent agent)
    {
        var chatClient = new AzureOpenAIClient(
            new Uri(agent.ProviderEndpoint),
            new ApiKeyCredential(agent.ProviderApiKey))
             .GetChatClient(agent.ProviderModelName)
             .AsIChatClient()
             .AsBuilder()
             .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
             .Build();

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(DateTimePlugin.GetCurrentDateTime)
        };

        return Create(chatClient, instructions: agent.Instructions, name: "NTG.Agent", tools: tools);
    }

    private AIAgent Create(IChatClient chatClient, string instructions, string name, List<AITool> tools)
    {
        var agent = new ChatClientAgent(chatClient,
            name: name,
            instructions: instructions,
            tools: tools)
            .AsBuilder()
            .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator")
            .Build();
        return agent;
    }
}
