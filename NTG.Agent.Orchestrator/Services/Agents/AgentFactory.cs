using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using NTG.Agent.AITools.SimpleTools;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Services.Agents.Clients;

namespace NTG.Agent.Orchestrator.Services.Agents;

public class AgentFactory : IAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly AgentDbContext _agentDbContext;
    private readonly RenderableToolCapture _renderableToolCapture;
    private readonly IServiceProvider _serviceProvider;
    public string ToolContext { get; set; } = string.Empty;

    private Guid DefaultAgentId = new Guid("31CF1546-E9C9-4D95-A8E5-3C7C7570FEC5");

    public AgentFactory(IConfiguration configuration, AgentDbContext agentDbContext, RenderableToolCapture renderableToolCapture, IServiceProvider serviceProvider)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _renderableToolCapture = renderableToolCapture ?? throw new ArgumentNullException(nameof(renderableToolCapture));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    private IAgentClientFactory ResolveClientFactory(string providerName) =>
        _serviceProvider.GetKeyedService<IAgentClientFactory>(providerName)
            ?? throw new NotSupportedException($"Agent provider '{providerName}' is not supported.");

    public async Task<AIAgent> CreateAgent(Guid agentId)
    {
        var agentConfig = await _agentDbContext.Agents
            .FirstOrDefaultAsync(a => a.Id == agentId && a.IsPublished && a.AgentKind == AgentKind.Outer)
            ?? throw new ArgumentException($"Agent with ID '{agentId}' not found.");

        return await CreateAgentFromConfigAsync(agentConfig);
    }

    // This agent is used for simple tasks, like summarization, naming the conversation, etc.
    // No tools are enabled for this agent
    // For simplicity, we use the sample LLM model with the default agent. You can use smaller model for cost saving.
    public async Task<AIAgent> CreateBasicAgent(string instructions)
    {
        var agentConfig = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == DefaultAgentId) ?? throw new ArgumentException($"Agent with ID '{DefaultAgentId}' not found.");

        var chatClient = ResolveClientFactory(agentConfig.ProviderName).CreateChatClient(agentConfig, enableThinking: false);
        return new ChatClientAgent(chatClient, instructions: instructions);
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

            if (agent.AgentKind == AgentKind.Outer)
            {
                var innerAgentTools = await GetInnerAgentToolsAsync(agent);
                tools.AddRange(innerAgentTools);
            }
        }

        return tools;
    }

    public async Task<List<AITool>> GetAvailableTools(Models.Agents.Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        // 1. Define built-in tools (static plugins)
        var allTools = new List<AITool>
        {
            AIFunctionFactory.Create(DateTimeTools.GetCurrentDateTime)
        };

        // 2. Add MCP tools (from remote MCP server). Renderable tools (e.g. get_weather) are wrapped so
        //    their result is captured for the browser to render — works whether this agent is the outer
        //    agent or an inner agent the outer one delegates to.
        if (!string.IsNullOrEmpty(agent.McpServer?.Trim()))
        {
            var mcpTools = await GetMcpToolsAsync(agent.McpServer);
            foreach (var tool in mcpTools)
            {
                if (tool is AIFunction fn && RenderableToolCapture.IsRenderable(fn.Name))
                {
                    allTools.Add(new CapturingAIFunction(fn, _renderableToolCapture));
                }
                else
                {
                    allTools.Add(tool);
                }
            }
        }

        return allTools;
    }

    private async Task<AIAgent> CreateAgentFromConfigAsync(Models.Agents.Agent agent)
    {
        var chatClient = ResolveClientFactory(agent.ProviderName).CreateChatClient(agent, enableThinking: true);
        var tools = await GetAgentToolsByAgentId(agent);
        return Create(chatClient, instructions: agent.Instructions, name: agent.Name, description: GetAgentDescription(agent), tools: tools);
    }

    private static string? GetAgentDescription(Models.Agents.Agent agent)
    {
        if (!string.IsNullOrWhiteSpace(agent.Description))
        {
            return agent.Description;
        }

        return string.IsNullOrWhiteSpace(agent.Instructions) ? null : agent.Instructions;
    }

    private async Task<List<AITool>> GetInnerAgentToolsAsync(Models.Agents.Agent outerAgent)
    {
        var bindings = await _agentDbContext.AgentInnerAgents
            .Where(b => b.OuterAgentId == outerAgent.Id && b.IsEnabled)
            .Select(b => b.InnerAgentId)
            .ToListAsync();

        if (bindings.Count == 0)
        {
            return [];
        }

        var innerAgents = await _agentDbContext.Agents
            .Where(a => bindings.Contains(a.Id) && a.AgentKind == AgentKind.Inner && a.IsPublished)
            .ToListAsync();

        var tools = new List<AITool>();
        foreach (var innerAgent in innerAgents)
        {
            var agent = await CreateAgentFromConfigAsync(innerAgent);
            tools.Add(agent.AsAIFunction());
        }

        return tools;
    }


    private static AIAgent Create(IChatClient chatClient, string instructions, string name, string? description, List<AITool> tools)
    {
        var agent = new ChatClientAgent(chatClient,
            name: name,
            instructions: instructions,
            description: description,
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
