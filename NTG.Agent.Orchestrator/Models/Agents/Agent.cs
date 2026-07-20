using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Models.Identity;

namespace NTG.Agent.Orchestrator.Models.Agents;

public class Agent
{
    public Agent()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Instructions { get; set; } = string.Empty;

    public Guid? ProviderId { get; set; }

    public string? ModelOverride { get; set; }

    public Provider? Provider { get; set; }

    public bool IsPublished { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>Whether this agent uses Fast or Thinking (reasoning) mode.</summary>
    public AgentMode Mode { get; set; } = AgentMode.Fast;

    public AgentKind AgentKind { get; set; } = AgentKind.Outer;

    public string? McpServer { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid OwnerUserId { get; set; }

    public User OwnerUser { get; set; } = null!;

    public Guid UpdatedByUserId { get; set; }

    public User UpdatedByUser { get; set; } = null!;

    public ICollection<AgentTools> AgentTools { get; set; } = new List<AgentTools>();

    /// <summary>Bindings where this agent is the outer agent.</summary>
    public ICollection<AgentInnerAgent> InnerAgentBindings { get; set; } = new List<AgentInnerAgent>();

    /// <summary>Bindings where this agent is used as an inner agent.</summary>
    public ICollection<AgentInnerAgent> OuterAgentBindings { get; set; } = new List<AgentInnerAgent>();

}
