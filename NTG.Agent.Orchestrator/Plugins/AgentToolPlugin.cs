using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Services.Agents;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

/// <summary>
/// Wraps an inner (document) agent as an AITool callable from a parent agent's chat.
///
/// Responsibility main's bare <c>agent.AsAIFunction()</c> wrapper does not cover:
/// re-checks role-gated access at call time (defense in depth — the inner agent is also
/// filtered at registration in <see cref="AgentFactory.GetInnerAgentToolsAsync"/>).
///
/// Inner agents have no knowledge base of their own (no container, no workspace, no
/// documents), so no knowledge/memory tool is attached here.
/// </summary>
public sealed class AgentToolPlugin
{
    private readonly AIAgent _childAgent;
    private readonly AgentAccessService _accessService;
    private readonly Guid _childAgentId;
    private readonly Guid? _userId;
    private readonly bool _isAdmin;
    private readonly string _toolName;
    private readonly string _toolDescription;

    public AgentToolPlugin(
        AIAgent childAgent,
        AgentAccessService accessService,
        Guid childAgentId,
        Guid? userId,
        bool isAdmin,
        string toolName,
        string toolDescription)
    {
        _childAgent = childAgent ?? throw new ArgumentNullException(nameof(childAgent));
        _accessService = accessService ?? throw new ArgumentNullException(nameof(accessService));
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
        // Defense in depth: re-check access at call time even though the tool was filtered
        // at registration. This is the critical permission gate.
        var hasAccess = await _accessService.HasAccessAsync(_childAgentId, _userId, _isAdmin, cancellationToken);
        if (!hasAccess)
        {
            return "You do not have permission to access this resource.";
        }

        var chatHistory = new List<ChatMessage>
        {
            new(ChatRole.User, query)
        };

        var result = await _childAgent.RunAsync(chatHistory, cancellationToken: cancellationToken);
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
