namespace NTG.Agent.Common.Dtos.Chats;

public class ChatMessageItem
{
    public Guid Id { get; set; }
    public bool IsAssistant { get; set; }
    public bool IsUser { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>Chain-of-thought reasoning produced by a Thinking-mode agent. Null for Fast-mode messages.</summary>
    public string? ThinkingContent { get; set; }
    public ReactionType Reaction { get; set; }
    public string UserComment { get; set; } = string.Empty;
}
