using NTG.Agent.Orchestrator.Models.Identity;

namespace NTG.Agent.Orchestrator.Models.Tags;

public class TagRole
{
    public TagRole()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
