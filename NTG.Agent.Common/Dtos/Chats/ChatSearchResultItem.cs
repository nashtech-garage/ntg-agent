namespace NTG.Agent.Common.Dtos.Chats;
public class ChatSearchResultItem
{
    public string Content { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsConversation { get; set; }
}
