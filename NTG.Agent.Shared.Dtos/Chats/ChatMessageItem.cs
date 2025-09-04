namespace NTG.Agent.Shared.Dtos.Chats;

public class ChatMessageItem
{
    public Guid Id { get; set; }
    public bool IsAssistant { get; set; }
    public bool IsUser { get; set; }
    public string Message { get; set; } = string.Empty;
    public ReactionType Reaction { get; set; }
    public string UserComment { get; set; } = string.Empty;
}
