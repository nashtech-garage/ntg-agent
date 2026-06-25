using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Services.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

/// <summary>
/// Wraps a linked child (document) agent as an AITool that can be called from a parent
/// agent's chat. Performs a role-gated access check at call time — if the current user
/// lacks access to the child agent, a refusal is returned and no document content leaks.
/// </summary>
public sealed class AgentToolPlugin
{
    private readonly IAgentFactory _agentFactory;
    private readonly AgentAccessService _accessService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly Guid _childAgentId;
    private readonly Guid? _userId;
    private readonly bool _isAdmin;
    private readonly string _toolName;
    private readonly string _toolDescription;

    public AgentToolPlugin(
        IAgentFactory agentFactory,
        AgentAccessService accessService,
        IKnowledgeService knowledgeService,
        Guid childAgentId,
        Guid? userId,
        bool isAdmin,
        string toolName,
        string toolDescription)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _accessService = accessService ?? throw new ArgumentNullException(nameof(accessService));
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _childAgentId = childAgentId;
        _userId = userId;
        _isAdmin = isAdmin;
        _toolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        _toolDescription = toolDescription ?? throw new ArgumentNullException(nameof(toolDescription));
    }

    [Description("Ask a question to a specialized document agent and get an answer")]
    public async Task<string> AskAsync(
        [Description("The question to ask the document agent")] string query,
        CancellationToken cancellationToken = default)
    {
        // Defense in depth: re-check access at call time even if the tool was
        // filtered at registration. This is the critical permission gate.
        var hasAccess = await _accessService.HasAccessAsync(_childAgentId, _userId, _isAdmin, cancellationToken);
        if (!hasAccess)
        {
            return "You do not have permission to access this resource.";
        }

        // Create the child agent (also throws AgentAccessDeniedException if access changed
        // between registration and call — defense in depth).
        var childAgent = await _agentFactory.CreateAgent(_childAgentId, _userId, _isAdmin);

        // The agent factory only attaches static-config tools (DateTime, MCP); the LightRAG
        // knowledge tool is a request-layer concern wired in AgentService for direct chat.
        // The tool path bypasses AgentService, so re-attach it here scoped to the CHILD agent's
        // workspace — otherwise the child answers from parametric knowledge, not its documents.
        // Tags are passed empty: LightRAG ignores tag filtering, so per-agent isolation is the scope.
        var memoryTool = new KnowledgePlugin(_knowledgeService, [], _childAgentId).AsAITool();
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Tools = [memoryTool] });

        // Run a single-turn chat against the child agent.
        var chatHistory = new List<ChatMessage>
        {
            new(ChatRole.User, query)
        };

        var result = await childAgent.RunAsync(chatHistory, options: runOptions, cancellationToken: cancellationToken);
        return result.Text ?? string.Empty;
    }

    public AITool AsAITool()
    {
        return AIFunctionFactory.Create(
            this.AskAsync,
            new AIFunctionFactoryOptions
            {
                Name = _toolName,
                Description = _toolDescription
            });
    }
}