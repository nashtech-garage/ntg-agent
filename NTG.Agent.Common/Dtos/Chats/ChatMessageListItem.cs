namespace NTG.Agent.Common.Dtos.Chats;
public class ChatMessageListItem
{
    public string Content { get; set; } = string.Empty;
    /// <summary>Chain-of-thought reasoning produced by a Thinking-mode agent. Null for Fast-mode messages.</summary>
    public string? ThinkingContent { get; set; }
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public ReactionType Reaction { get; set; }
    public string UserComment { get; set; } = string.Empty;
}
