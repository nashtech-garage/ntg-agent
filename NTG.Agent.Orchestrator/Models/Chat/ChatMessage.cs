using NTG.Agent.Shared.Dtos.Enums;
using NTG.Agent.Shared.Dtos.Chats;

namespace NTG.Agent.Orchestrator.Models.Chat;

public class ChatMessage
{
    public ChatMessage()
    {
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
    public bool IsSummary { get; set; } = false;
    public ReactionType Reaction { get; set; }
    public string UserComment { get; set; } = string.Empty;
}


