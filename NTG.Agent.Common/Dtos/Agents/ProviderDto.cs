namespace NTG.Agent.Common.Dtos.Agents;

public class ProviderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DefaultModel { get; set; }
    public int AgentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
