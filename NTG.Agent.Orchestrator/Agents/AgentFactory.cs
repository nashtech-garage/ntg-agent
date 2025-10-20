using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.KernelMemory.Context;
using ModelContextProtocol.Client;
using NTG.Agent.AITools.SimpleTools;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Shared.Dtos.Agents;
using OpenAI;
using System.ClientModel;

namespace NTG.Agent.Orchestrator.Agents;

public class AgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly AgentDbContext _agentDbContext;
    public string ToolContext { get; set; } = string.Empty;

    private Guid DefaultAgentId = new Guid("31CF1546-E9C9-4D95-A8E5-3C7C7570FEC5");

    public AgentFactory(IConfiguration configuration, AgentDbContext agentDbContext)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
    }

    public async Task<AIAgent> CreateAgent(Guid agentId)
    {
        var agentConfig = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == agentId) ?? throw new ArgumentException($"Agent with ID '{agentId}' not found.");
        string agentProvider = agentConfig.ProviderName;
        return agentProvider switch
        {
            "GitHubModel" => await CreateOpenAIAgentAsync(agentConfig),
            "AzureOpenAI" => await CreateAzureOpenAIAgentAsync(agentConfig),
            _ => throw new NotSupportedException($"Agent provider '{agentProvider}' is not supported."),
        };
    }

    // This agent is used for simple tasks, like summarization, naming the conversation, etc.
    // No tools are enabled for this agent
    // For simplicity, we use the sample LLM model with the default agent. You can use smaller model for cost saving.
    public async Task<AIAgent> CreateBasicAgent(string instructions)
    {
        var agentConfig = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == DefaultAgentId) ?? throw new ArgumentException($"Agent with ID '{DefaultAgentId}' not found.");
        string agentProvider = agentConfig.ProviderName;
        return agentProvider switch
        {
            "GitHubModel" => CreateBasicOpenAIAgent(agentConfig, instructions),
            "AzureOpenAI" => CreateBasicAzureOpenAIAgent(agentConfig, instructions),
            _ => throw new NotSupportedException($"Agent provider '{agentProvider}' is not supported."),
        };
    }

    private AIAgent CreateBasicOpenAIAgent(Models.Agents.Agent agentConfig, string instructions)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(agentConfig.ProviderEndpoint)
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential(agentConfig.ProviderApiKey), clientOptions);
        var agent = openAiClient.GetChatClient(agentConfig.ProviderModelName).CreateAIAgent(instructions: instructions);
        return agent;
    }

    private AIAgent CreateBasicAzureOpenAIAgent(Models.Agents.Agent agentConfig, string instructions)
    {
        var agent = new AzureOpenAIClient(
             new Uri(agentConfig.ProviderEndpoint),
             new ApiKeyCredential(agentConfig.ProviderApiKey))
               .GetChatClient(agentConfig.ProviderModelName)
               .CreateAIAgent(instructions: instructions);
        return agent;
    }

    private async Task<AIAgent> CreateOpenAIAgentAsync(Models.Agents.Agent agent)
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

        var tools = await GetAgentToolsByAgentId(agent);

        return Create(chatClient, instructions: agent.Instructions, name: "NTG.Agent", tools: tools);
    }

    private async Task<AIAgent> CreateAzureOpenAIAgentAsync(Models.Agents.Agent agent)
    {
        var chatClient = new AzureOpenAIClient(
            new Uri(agent.ProviderEndpoint),
            new ApiKeyCredential(agent.ProviderApiKey))
             .GetChatClient(agent.ProviderModelName)
             .AsIChatClient()
             .AsBuilder()
             .UseFunctionInvocation()
             .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
             .Build();

        var tools = await GetAgentToolsByAgentId(agent);

        return Create(chatClient, instructions: agent.Instructions, name: "NTG.Agent", tools: tools);
    }

    private async Task<List<AITool>> GetAgentToolsByAgentId(Models.Agents.Agent agent)
    {
        var tools = new List<AITool>();
        if (agent != null)
        {
            var allTools = await GetAvailableTools(agent);

            await _agentDbContext.Entry(agent)
                .Collection(a => a.AgentTools)
                .LoadAsync();

            var enabledToolNames = agent.AgentTools
                .Where(t => t.IsEnabled)
                .Select(t => t.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            tools = allTools
                .Where(t => enabledToolNames.Contains(t.Name))
                .ToList();
        }

        return tools;
    }

    public async Task<List<AITool>> GetAvailableTools(Models.Agents.Agent agent)
    {
        if (agent is null)
            throw new ArgumentNullException(nameof(agent));

        // 1. Define built-in tools (static plugins)
        var allTools = new List<AITool>
        {
            AIFunctionFactory.Create(DateTimeTools.GetCurrentDateTime)
        };

        // 2. Add MCP tools (from remote MCP server)
        if (!string.IsNullOrEmpty(agent.McpServer?.Trim()))
        {
            var mcpTools = await GetMcpToolsAsync(agent.McpServer);
            allTools.AddRange(mcpTools);
        }

        return allTools;
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

    public async Task<IEnumerable<AITool>> GetMcpToolsAsync(string endpoint)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "ntgmcpserver",
            Endpoint = new Uri(endpoint),
            ConnectionTimeout = TimeSpan.FromMinutes(2)
        });

        var mcpClient = await McpClient.CreateAsync(transport);

        var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);

        return tools.Cast<AITool>();
    }
}
