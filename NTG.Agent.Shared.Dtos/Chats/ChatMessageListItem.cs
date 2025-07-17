namespace NTG.Agent.Shared.Dtos.Chats;
public class ChatMessageListItem
{
    public string Content { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public int Role { get; set; }
}
