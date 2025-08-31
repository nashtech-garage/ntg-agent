namespace NTG.Agent.Orchestrator.Models.Tags;

public class TagRole
{
    public TagRole()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    public Guid Id { get; set; }
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
    public Guid RoleId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
