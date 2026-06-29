using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using NTG.Agent.AITools.SimpleTools;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.Knowledge;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using OpenAI.Chat;

namespace NTG.Agent.Orchestrator.Services.Agents;

public class AgentFactory : IAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private readonly AgentAccessService _agentAccessService;
    public string ToolContext { get; set; } = string.Empty;

    private Guid DefaultAgentId = new Guid("31CF1546-E9C9-4D95-A8E5-3C7C7570FEC5");

    public AgentFactory(IConfiguration configuration, AgentDbContext agentDbContext, IKnowledgeService knowledgeService, AgentAccessService agentAccessService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _agentAccessService = agentAccessService ?? throw new ArgumentNullException(nameof(agentAccessService));
    }

    public async Task<AIAgent> CreateAgent(Guid agentId)
    {
        var agentConfig = await _agentDbContext.Agents
            .FirstOrDefaultAsync(a => a.Id == agentId && a.IsPublished && a.AgentKind == AgentKind.Outer)
            ?? throw new ArgumentException($"Agent with ID '{agentId}' not found.");

        return await CreateAgentFromConfigAsync(agentConfig);
    }

    public async Task<AIAgent> CreateAgent(Guid agentId, Guid? userId, bool isAdmin)
    {
        var agentConfig = await _agentDbContext.Agents.FirstOrDefaultAsync(a =>
            a.Id == agentId
            && a.IsPublished
            && (a.OwnerUserId == userId || isAdmin
                || _agentDbContext.AgentRoles.Any(ar =>
                    ar.AgentId == a.Id
                    && _agentDbContext.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == ar.RoleId))))
            ?? throw new AgentAccessDeniedException(agentId);

        return await CreateAgentFromConfigAsync(agentConfig, userId, isAdmin);
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
            "GoogleGemini" => CreateBasicOpenAIAgent(agentConfig, instructions),
            "OpenAI" => CreateBasicOpenAIAgent(agentConfig, instructions),
            "AzureOpenAI" => CreateBasicAzureOpenAIAgent(agentConfig, instructions),
            "Anthropic" => CreateBasicAnthropicAgent(agentConfig, instructions),
            _ => throw new NotSupportedException($"Agent provider '{agentProvider}' is not supported."),
        };
    }

    private static ChatClientAgent CreateBasicOpenAIAgent(Models.Agents.Agent agentConfig, string instructions)
    {
        // ProviderEndpoint is optional for standard OpenAI; GitHub Models and Google Gemini require a custom endpoint.
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(agentConfig.ProviderEndpoint))
        {
            clientOptions.Endpoint = new Uri(agentConfig.ProviderEndpoint);
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(agentConfig.ProviderApiKey), clientOptions);
        var agent = openAiClient.GetChatClient(agentConfig.ProviderModelName).AsAIAgent(instructions: instructions);
        return agent;
    }

    private static ChatClientAgent CreateBasicAnthropicAgent(Models.Agents.Agent agentConfig, string instructions)
    {
        // Uses the official Anthropic SDK (Anthropic NuGet package) which includes Microsoft.Extensions.AI
        // integration via the AsIChatClient() extension method defined in the Microsoft.Extensions.AI namespace.
        var chatClient = new AnthropicClient(new ClientOptions { ApiKey = agentConfig.ProviderApiKey })
            .AsIChatClient(defaultModelId: agentConfig.ProviderModelName);

        return new ChatClientAgent(chatClient, instructions: instructions);
    }

    private static ChatClientAgent CreateBasicAzureOpenAIAgent(Models.Agents.Agent agentConfig, string instructions)
    {
        var agent = new AzureOpenAIClient(
             new Uri(agentConfig.ProviderEndpoint),
             new ApiKeyCredential(agentConfig.ProviderApiKey))
               .GetChatClient(agentConfig.ProviderModelName)
               .AsAIAgent(instructions: instructions);
        return agent;
    }

    private async Task<AIAgent> CreateOpenAIAgentAsync(Models.Agents.Agent agent, Guid? userId = null, bool isAdmin = false)
    {
        IChatClient chatClient;

        if (agent.Mode == AgentMode.Thinking)
        {
            // o-series reasoning models (o3, o4-mini, etc.) require the Responses API (/v1/responses).
            // o.Reasoning surfaces chain-of-thought tokens as TextReasoningContent in the stream.
            // See: https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/02-agents/AgentWithOpenAI/Agent_OpenAI_Step02_Reasoning/Program.cs
#pragma warning disable OPENAI001
            chatClient = new OpenAIClient(new ApiKeyCredential(agent.ProviderApiKey))
                .GetResponsesClient()
                .AsIChatClient(agent.ProviderModelName)
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                .ConfigureOptions(o =>
                {
                    o.Reasoning = new()
                    {
                        Effort = ReasoningEffort.Medium,
                        Output = ReasoningOutput.Full,
                    };
                })
                .Build();
#pragma warning restore OPENAI001
        }
        else
        {
            // Standard models use Chat Completions API.
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(agent.ProviderEndpoint) };
            chatClient = new OpenAIClient(new ApiKeyCredential(agent.ProviderApiKey), clientOptions)
                .GetChatClient(agent.ProviderModelName)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                .Build();
        }

        var tools = await GetAgentToolsByAgentId(agent, userId, isAdmin);
        return Create(chatClient, instructions: agent.Instructions, name: agent.Name, description: GetAgentDescription(agent), tools: tools);
    }

    private async Task<AIAgent> CreateAnthropicAgentAsync(Models.Agents.Agent agent, Guid? userId = null, bool isAdmin = false)
    {
        IChatClient chatClient;

        if (agent.Mode == AgentMode.Thinking)
        {
            // Extended thinking surfaces chain-of-thought as ThinkingContent items in the streaming response.
            // Requires a compatible Claude model (e.g. claude-3-7-sonnet or later).
            // The Anthropic MEA adapter reads RawRepresentationFactory to build the raw MessageCreateParams,
            // which is the only supported way to pass the Thinking configuration.
            // budgetTokens controls max reasoning tokens (must be ≥1024 and less than MaxTokens).
            // See: https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/02-agents/AgentWithAnthropic/Agent_Anthropic_Step02_Reasoning/Program.cs
            const int maxTokens = 4096;
            const int thinkingTokens = 2048;
            chatClient = new AnthropicClient(new ClientOptions { ApiKey = agent.ProviderApiKey })
                .AsIChatClient(defaultModelId: agent.ProviderModelName)
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                .ConfigureOptions(o =>
                {
                    o.RawRepresentationFactory = _ => new MessageCreateParams
                    {
                        Model = agent.ProviderModelName,
                        MaxTokens = o.MaxOutputTokens ?? maxTokens,
                        Messages = [],
                        Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(budgetTokens: thinkingTokens))
                    };
                })
                .Build();
        }
        else
        {
            chatClient = new AnthropicClient(new ClientOptions { ApiKey = agent.ProviderApiKey })
                .AsIChatClient(defaultModelId: agent.ProviderModelName)
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                .Build();
        }

        var tools = await GetAgentToolsByAgentId(agent, userId, isAdmin);

        return Create(chatClient, instructions: agent.Instructions, name: agent.Name, description: GetAgentDescription(agent), tools: tools);
    }

    private async Task<AIAgent> CreateAzureOpenAIAgentAsync(Models.Agents.Agent agent, Guid? userId = null, bool isAdmin = false)
    {
        IChatClient chatClient;

        if (agent.Mode == AgentMode.Thinking)
        {
            // See: https://github.com/rwjdk/MicrosoftAgentFrameworkSamples/blob/main/src/OpenAIResponsesApi.ReasoningSummary/Program.cs
#pragma warning disable OPENAI001
            chatClient = new AzureOpenAIClient(
                    new Uri(agent.ProviderEndpoint),
                    new ApiKeyCredential(agent.ProviderApiKey))
                .GetResponsesClient()
                .AsIChatClient(agent.ProviderModelName)
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                .ConfigureOptions(o =>
                {
                    o.RawRepresentationFactory = _ => new CreateResponseOptions
                    {
                        ReasoningOptions = new ResponseReasoningOptions
                        {
                            ReasoningEffortLevel = ResponseReasoningEffortLevel.High,
                            ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Detailed,
                        }
                    };
                })
                .Build();
#pragma warning restore OPENAI001
        }
        else
        {
            chatClient = new AzureOpenAIClient(
                new Uri(agent.ProviderEndpoint),
                new ApiKeyCredential(agent.ProviderApiKey))
                .GetChatClient(agent.ProviderModelName)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                .Build();
        }

        var tools = await GetAgentToolsByAgentId(agent, userId, isAdmin);
        return Create(chatClient, instructions: agent.Instructions, name: agent.Name, description: GetAgentDescription(agent), tools: tools);
    }

    private async Task<List<AITool>> GetAgentToolsByAgentId(Models.Agents.Agent agent, Guid? userId = null, bool isAdmin = false)
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
                var innerAgentTools = await GetInnerAgentToolsAsync(agent, userId, isAdmin);
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

        // 2. Add MCP tools (from remote MCP server)
        if (!string.IsNullOrEmpty(agent.McpServer?.Trim()))
        {
            var mcpTools = await GetMcpToolsAsync(agent.McpServer);
            allTools.AddRange(mcpTools);
        }

        return allTools;
    }

    private async Task<AIAgent> CreateAgentFromConfigAsync(Models.Agents.Agent agent, Guid? userId = null, bool isAdmin = false)
    {
        string agentProvider = agent.ProviderName;
        return agentProvider switch
        {
            "GitHubModel" => await CreateOpenAIAgentAsync(agent, userId, isAdmin),
            "GoogleGemini" => await CreateOpenAIAgentAsync(agent, userId, isAdmin),
            "OpenAI" => await CreateOpenAIAgentAsync(agent, userId, isAdmin),
            "AzureOpenAI" => await CreateAzureOpenAIAgentAsync(agent, userId, isAdmin),
            "Anthropic" => await CreateAnthropicAgentAsync(agent, userId, isAdmin),
            _ => throw new NotSupportedException($"Agent provider '{agentProvider}' is not supported."),
        };
    }

    private static string? GetAgentDescription(Models.Agents.Agent agent)
    {
        if (!string.IsNullOrWhiteSpace(agent.Description))
        {
            return agent.Description;
        }

        return string.IsNullOrWhiteSpace(agent.Instructions) ? null : agent.Instructions;
    }

    private async Task<List<AITool>> GetInnerAgentToolsAsync(Models.Agents.Agent outerAgent, Guid? userId = null, bool isAdmin = false)
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
            // Per-role access gate: only expose an inner agent the caller may use.
            // The plugin re-checks access at call time as defense in depth.
            if (!await _agentAccessService.HasAccessAsync(innerAgent.Id, userId, isAdmin))
            {
                continue;
            }

            var child = await CreateAgentFromConfigAsync(innerAgent, userId, isAdmin);
            var toolName = ToToolName(innerAgent.Name, innerAgent.Id);
            var toolDescription = !string.IsNullOrWhiteSpace(innerAgent.Description)
                ? innerAgent.Description
                : (!string.IsNullOrWhiteSpace(innerAgent.Instructions) ? innerAgent.Instructions : innerAgent.Name);

            // Wrap the child so that (a) access is re-checked at call time and (b) the child's
            // own LightRAG knowledge tool is attached (scoped to its workspace) — the bare
            // AsAIFunction() path would let the child answer only from parametric knowledge.
            var plugin = new AgentToolPlugin(
                child, _agentAccessService, _knowledgeService,
                innerAgent.Id, userId, isAdmin, toolName, toolDescription);
            tools.Add(plugin.AsAITool());
        }

        return tools;
    }

    // Function/tool names must be a safe identifier; derive one from the agent name and
    // fall back to a stable id-based name when it sanitizes to empty.
    private static string ToToolName(string? name, Guid agentId)
    {
        var sanitized = new string((name ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_')
            .ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? $"agent_{agentId:N}" : sanitized;
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
