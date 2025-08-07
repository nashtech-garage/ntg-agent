namespace NTG.Agent.Shared.Dtos.SharedConversations;
public class SharedMessageDto
{
    public Guid Id { get; set; }
    public Guid SharedConversationId { get; set; }
    public string Content { get; set; } = null!;
    public int Role { get; set; }
}
