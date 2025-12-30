namespace NTG.Agent.Orchestrator.Models.Memory;

public class UserMemory
{
    public UserMemory()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? EmbeddingId { get; set; }
    public string Category { get; set; } = "general";
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int AccessCount { get; set; } = 0;
    public DateTime? LastAccessedAt { get; set; }
}
