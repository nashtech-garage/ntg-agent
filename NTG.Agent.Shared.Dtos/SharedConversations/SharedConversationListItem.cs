using NTG.Agent.Shared.Dtos.Enums;

namespace NTG.Agent.Shared.Dtos.SharedConversations;
public class SharedConversationListItem
{
    public Guid Id { get; set; }
    public Guid OriginalConversationId { get; set; }
    public Guid UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Name { get; set; }
    public SharedType Type { get; set; }
}
