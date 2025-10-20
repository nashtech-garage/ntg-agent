using NTG.Agent.Orchestrator.Models.Agents;

namespace NTG.Agent.Shared.Dtos.Agents;

public class AgentToolDto
{
    public Guid Id { get; set; }

    public Guid AgentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public AgentToolType AgentToolType { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}