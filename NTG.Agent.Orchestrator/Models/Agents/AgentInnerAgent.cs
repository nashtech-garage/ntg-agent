namespace NTG.Agent.Orchestrator.Models.Agents;

public class AgentInnerAgent
{
    public AgentInnerAgent()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid OuterAgentId { get; set; }

    public Agent OuterAgent { get; set; } = null!;

    public Guid InnerAgentId { get; set; }

    public Agent InnerAgent { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
