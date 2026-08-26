namespace NTG.Agent.Orchestrator.Models.Skills;

/// <summary>
/// Binds a <see cref="Skill"/> to an agent. Only skills bound and enabled for an agent appear in
/// the catalog injected into that agent's runs, mirroring how <c>AgentInnerAgent</c> scopes
/// inner-agent tools.
/// </summary>
/// <remarks>
/// The agent type is fully qualified throughout: the root <c>NTG.Agent</c> namespace shadows the
/// <c>Agent</c> type name from outside <c>Models.Agents</c>.
/// </remarks>
public class AgentSkill
{
    public AgentSkill()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid AgentId { get; set; }

    public Models.Agents.Agent Agent { get; set; } = null!;

    public Guid SkillId { get; set; }

    public Skill Skill { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
