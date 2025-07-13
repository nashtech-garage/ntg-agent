namespace NTG.Agent.Orchestrator.Models.Chat;

public class ChatMessage
{
    public ChatMessage()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public ChatRole Role { get; set; } = ChatRole.User;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
