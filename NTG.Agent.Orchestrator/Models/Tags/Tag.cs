namespace NTG.Agent.Orchestrator.Models.Tags;

public class Tag
{
    public Tag()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? AgentId { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
