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

    public string Instructions { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string ProviderModelName { get; set; } = string.Empty;

    public string ProviderEndpoint { get; set; } = string.Empty;

    public string ProviderApiKey { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public bool IsDefault { get; set; }

    public string? McpServer { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid OwnerUserId { get; set; }

    public User OwnerUser { get; set; } = null!;

    public Guid UpdatedByUserId { get; set; }

    public User UpdatedByUser { get; set; } = null!;

    public ICollection<AgentTools> AgentTools { get; set; } = new List<AgentTools>();

}
