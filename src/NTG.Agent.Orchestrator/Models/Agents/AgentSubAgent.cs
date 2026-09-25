namespace NTG.Agent.Orchestrator.Models.Agents;

public class AgentSubAgent
{
    public AgentSubAgent()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid AgentId { get; set; }

    public Agent Agent { get; set; } = null!;

    public Guid SubAgentId { get; set; }

    public Agent SubAgent { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
