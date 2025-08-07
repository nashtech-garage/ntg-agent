namespace NTG.Agent.Orchestrator.Models.Chat;

public class SharedChatMessage
{
    public SharedChatMessage()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    public Guid Id { get; set; }
    public Guid SharedConversationId { get; set; }
    public string Content { get; set; } = null!;
    public ChatRole Role { get; set; } = ChatRole.User;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public SharedConversation SharedConversation { get; set; } = null!;
}

