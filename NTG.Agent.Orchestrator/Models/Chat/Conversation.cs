namespace NTG.Agent.Orchestrator.Models.Chat;

public class Conversation
{
    public Conversation()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

}
