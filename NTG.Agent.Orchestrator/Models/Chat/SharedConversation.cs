using NTG.Agent.Shared.Dtos.Enums;

namespace NTG.Agent.Orchestrator.Models.Chat;

public class SharedConversation
{
    public SharedConversation()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        ExpiresAt = DateTime.MaxValue;
        Messages = new List<SharedChatMessage>();
    }
    public Guid Id { get; set; }
    public Guid OriginalConversationId { get; set; }
    public Guid UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Name { get; set; }
    public SharedType Type { get; set; }
    public ICollection<SharedChatMessage> Messages { get; set; } = null!;
}
