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

    public string ProviderName { get; set; } = string.Empty;

    public string ProviderModelName { get; set; } = string.Empty;

    public string ProviderEndpoint { get; set; } = string.Empty;

    public string ProviderApiKey { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>Whether this agent uses Fast or Thinking (reasoning) mode.</summary>
    public AgentMode Mode { get; set; } = AgentMode.Fast;

    public AgentKind AgentKind { get; set; } = AgentKind.Outer;

    public string? McpServer { get; set; } = string.Empty;

    /// <summary>
    /// Host port of this agent's dedicated LightRAG container (lightrag-agent-{Id}).
    /// Null until the container has been provisioned. Allocated dynamically at
    /// agent creation and self-healed by <c>LightRagReconcilerHostedService</c> on restart.
    /// </summary>
    public int? LightRagPort { get; set; }

    public AgentProvisioningStatus ProvisioningStatus { get; set; } = AgentProvisioningStatus.Provisioning;

    /// <summary>Failure reason surfaced to the UI when <see cref="ProvisioningStatus"/> is
    /// <see cref="AgentProvisioningStatus.Failed"/>.</summary>
    public string? ProvisioningError { get; set; }

    /// <summary>When the last provisioning transition to Ready/Failed occurred</summary>
    public DateTime? ProvisionedAt { get; set; }

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
