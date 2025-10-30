namespace NTG.Agent.Common.Dtos.Chats;
public class ChatMessageListItem
{
    public string Content { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public ReactionType Reaction { get; set; }
    public string UserComment { get; set; } = string.Empty;
}
