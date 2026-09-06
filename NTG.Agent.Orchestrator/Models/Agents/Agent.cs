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

    /// <summary>Sampling temperature for generation. Null = provider/model default.</summary>
    public double? Temperature { get; set; }

    /// <summary>Maximum output tokens for generation. Null = provider/model default.</summary>
    public int? MaxOutputTokens { get; set; }

    public AgentKind AgentKind { get; set; } = AgentKind.Outer;

    public string? McpServer { get; set; } = string.Empty;

    public AgentProvisioningStatus ProvisioningStatus { get; set; } = AgentProvisioningStatus.Provisioning;

    /// <summary>Failure reason surfaced to the UI when <see cref="ProvisioningStatus"/> is
    /// <see cref="AgentProvisioningStatus.Failed"/>.</summary>
    public string? ProvisioningError { get; set; }

    /// <summary>When the last provisioning transition to Ready/Failed occurred</summary>
    public DateTime? ProvisionedAt { get; set; }

    /// <summary>
    /// The agent that owns the LightRAG knowledge base this agent uses.
    /// <c>null</c> means this agent owns its own KB; a non-null value means it is a guest in that
    /// agent's KB. An owner always has this set to <c>null</c> — guests never own, so a join target
    /// must itself be an owner (no chaining).
    /// Always <c>null</c> for <see cref="AgentKind.Inner"/> agents, which have no knowledge base.
    /// </summary>
    public Guid? KnowledgeOwnerAgentId { get; set; }

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

    /// <summary>Agent Skills bound to this agent. Only enabled bindings reach the model's catalog.</summary>
    public ICollection<Skills.AgentSkill> SkillBindings { get; set; } = new List<Skills.AgentSkill>();

}
