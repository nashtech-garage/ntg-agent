namespace NTG.Agent.Orchestrator.Models.Agents;

/// <summary>
/// Represents a directed handoff connection between two agents.
/// Each row is one edge in the handoff graph: SourceAgent can hand off to TargetAgent.
/// Bidirectional handoffs require two rows (A→B and B→A).
/// </summary>
public class AgentHandoff
{
    public AgentHandoff()
    {
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    /// <summary>The agent that initiates the handoff.</summary>
    public Guid SourceAgentId { get; set; }

    public Agent SourceAgent { get; set; } = null!;

    /// <summary>The agent that receives the handoff.</summary>
    public Guid TargetAgentId { get; set; }

    public Agent TargetAgent { get; set; } = null!;

    /// <summary>Optional description of when/why this handoff should occur.</summary>
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
