using Microsoft.AspNetCore.Identity;
using NTG.Agent.Orchestrator.Models.Identity;

namespace NTG.Agent.Orchestrator.Models.Agents;

public class AgentTools
{
    public AgentTools()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid AgentId { get; set; }

    public Agent Agent { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = false;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

}
