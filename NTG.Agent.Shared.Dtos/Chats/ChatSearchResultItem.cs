namespace NTG.Agent.Shared.Dtos.Chats;
public class ChatSearchResultItem
{
    public string Content { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsConversation { get; set; }
}
