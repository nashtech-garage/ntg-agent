namespace NTG.Agent.Shared.Dtos.Chats;
public class ChatSearchResultItem
{
    public string Content { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public int Role { get; set; }
    public bool IsConversation { get; set; }
}
