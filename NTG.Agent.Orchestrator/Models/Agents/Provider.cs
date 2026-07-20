using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Models.Agents;

public class Provider
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DefaultModel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
}
