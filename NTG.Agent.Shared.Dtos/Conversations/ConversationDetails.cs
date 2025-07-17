using NTG.Agent.Shared.Dtos.Chats;

namespace NTG.Agent.Shared.Dtos.Conversations;

public class ConversationDetails
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
}