using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using NTG.Agent.AITools.SimpleTools;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using OpenAI.Chat;

namespace NTG.Agent.Orchestrator.Services.Agents;

public class AgentFactory : IAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly AgentDbContext _agentDbContext;
    private readonly RenderableToolCapture _renderableToolCapture;
    public string ToolContext { get; set; } = string.Empty;

    private Guid DefaultAgentId = new Guid("31CF1546-E9C9-4D95-A8E5-3C7C7570FEC5");

    public AgentFactory(IConfiguration configuration, AgentDbContext agentDbContext, RenderableToolCapture renderableToolCapture)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _renderableToolCapture = renderableToolCapture ?? throw new ArgumentNullException(nameof(renderableToolCapture));
    }

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
        // The Azure resource is reached through its OpenAI-compatible v1 surface
        // (ProviderEndpoint ends in /openai/v1), so we use the plain OpenAIClient rather than
        // AzureOpenAIClient — the latter targets the legacy api-version deployment API and cannot
        // address /openai/v1. The same key is sent as a Bearer token, which that surface accepts.
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(agentConfig.ProviderEndpoint) };
        var agent = new OpenAIClient(new ApiKeyCredential(agentConfig.ProviderApiKey), clientOptions)
               .GetChatClient(agentConfig.ProviderModelName)
               .AsAIAgent(instructions: instructions);
        return agent;
    }

    private async Task<AIAgent> CreateOpenAIAgentAsync(Models.Agents.Agent agent)
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

        var tools = await GetAgentToolsByAgentId(agent);
        return Create(chatClient, instructions: agent.Instructions, name: agent.Name, description: GetAgentDescription(agent), tools: tools);
    }

    private async Task<AIAgent> CreateAnthropicAgentAsync(Models.Agents.Agent agent)
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

        var tools = await GetAgentToolsByAgentId(agent);

        return Create(chatClient, instructions: agent.Instructions, name: agent.Name, description: GetAgentDescription(agent), tools: tools);
    }

    /// <summary>
    /// Whether a model surfaces reasoning through the Chat Completions API (top-level
    /// <c>reasoning_effort</c>) rather than the Responses API (<c>reasoning.effort</c>).
    /// </summary>
    /// <remarks>
    /// DeepSeek reasoning models reject the Responses API reasoning parameter (HTTP 400
    /// <c>unsupported_parameter</c>) and instead expose their chain-of-thought as
    /// <c>reasoning_content</c> over Chat Completions. gpt-5.x / o-series use the Responses API.
    /// Detection is by model name because the agent configuration carries no capability flag.
    /// </remarks>
    private static bool UsesChatCompletionsReasoning(string modelName) =>
        modelName.Contains("deepseek", StringComparison.OrdinalIgnoreCase);

    private async Task<AIAgent> CreateAzureOpenAIAgentAsync(Models.Agents.Agent agent)
    {
        IChatClient chatClient;

        if (agent.Mode == AgentMode.Thinking)
        {
            // Both reasoning paths address the OpenAI-compatible v1 surface via OpenAIClient (not
            // AzureOpenAIClient, which cannot reach /openai/v1 — see CreateBasicAzureOpenAIAgent).
#pragma warning disable OPENAI001
            var thinkingClientOptions = new OpenAIClientOptions { Endpoint = new Uri(agent.ProviderEndpoint) };
            var thinkingClient = new OpenAIClient(new ApiKeyCredential(agent.ProviderApiKey), thinkingClientOptions);

            if (UsesChatCompletionsReasoning(agent.ProviderModelName))
            {
                // DeepSeek-family reasoning: Chat Completions with a top-level reasoning_effort.
                // The chain-of-thought returns as reasoning_content, which Microsoft.Extensions.AI
                // maps to TextReasoningContent — the same type the Responses path emits, so the
                // streaming/UI layer (AgentService) needs no change.
                chatClient = thinkingClient
                    .GetChatClient(agent.ProviderModelName)
                    .AsIChatClient()
                    .AsBuilder()
                    .UseFunctionInvocation()
                    .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                    .ConfigureOptions(o =>
                    {
                        o.RawRepresentationFactory = _ => new ChatCompletionOptions
                        {
                            ReasoningEffortLevel = ChatReasoningEffortLevel.High,
                        };
                    })
                    .Build();
            }
            else
            {
                // gpt-5.x / o-series reasoning: Responses API (/openai/v1/responses) with reasoning.effort.
                // See: https://github.com/rwjdk/MicrosoftAgentFrameworkSamples/blob/main/src/OpenAIResponsesApi.ReasoningSummary/Program.cs
                chatClient = thinkingClient
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
            }
#pragma warning restore OPENAI001
        }
        else
        {
            // Chat Completions over the same v1 surface (/openai/v1/chat/completions) via OpenAIClient.
            var fastClientOptions = new OpenAIClientOptions { Endpoint = new Uri(agent.ProviderEndpoint) };
            chatClient = new OpenAIClient(
                new ApiKeyCredential(agent.ProviderApiKey),
                fastClientOptions)
                .GetChatClient(agent.ProviderModelName)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: "NTG.Agent.Orchestrator", configure: (cfg) => cfg.EnableSensitiveData = true)
                .Build();
        }

        var tools = await GetAgentToolsByAgentId(agent);
        return Create(chatClient, instructions: agent.Instructions, name: agent.Name, description: GetAgentDescription(agent), tools: tools);
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
        string agentProvider = agent.ProviderName;
        return agentProvider switch
        {
            "GitHubModel" => await CreateOpenAIAgentAsync(agent),
            "GoogleGemini" => await CreateOpenAIAgentAsync(agent),
            "OpenAI" => await CreateOpenAIAgentAsync(agent),
            "AzureOpenAI" => await CreateAzureOpenAIAgentAsync(agent),
            "Anthropic" => await CreateAnthropicAgentAsync(agent),
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
