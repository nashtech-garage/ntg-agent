using System.ComponentModel.DataAnnotations;

namespace NTG.Agent.Common.Dtos.Agents;

public class AgentDetail
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Agent Name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Agent Name must be between 1 and 200 characters")]
    public string Name { get; set; }

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }

    public string? Instructions { get; set; }

    // Provider reference (replaces old ProviderName/Endpoint/ApiKey fields)
    public Guid? ProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderType { get; set; }
    public string? ModelOverride { get; set; }

    public bool IsDefault { get; set; }
    public bool IsPublished { get; set; }
    public string? McpServer { get; set; }

    /// <summary>Determines whether this agent is an Outer (user-facing) or Inner (tool) agent.</summary>
    public AgentKind AgentKind { get; set; } = AgentKind.Outer;

    /// <summary>Determines whether this agent uses Fast or Thinking (reasoning) mode.</summary>
    public AgentMode Mode { get; set; } = AgentMode.Fast;

    public string ToolCount { get; set; } = "0";

    public AgentDetail()
    {
        Name = string.Empty;
    }
}
