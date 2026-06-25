using NTG.Agent.Common.Dtos.Agents;

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

    public Agent Agent { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public AgentToolType AgentToolType { get; set; } = AgentToolType.BuiltIn;

    /// <summary>
    /// For <see cref="AgentToolType.Agent"/> tools, the ID of the linked child (document) agent.
    /// Null for BuiltIn and MCP tool types.
    /// </summary>
    public Guid? LinkedAgentId { get; set; }

    public Agent? LinkedAgent { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

}
