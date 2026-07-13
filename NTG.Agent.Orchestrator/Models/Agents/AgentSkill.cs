namespace NTG.Agent.Orchestrator.Models.Agents;

/// <summary>
/// Per-agent enablement of an Agent Skill hosted on the agent's MCP server.
/// Skills are keyed by frontmatter name; description is denormalized for display.
/// </summary>
public class AgentSkill
{
    public AgentSkill()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid AgentId { get; set; }

    public Agent Agent { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
