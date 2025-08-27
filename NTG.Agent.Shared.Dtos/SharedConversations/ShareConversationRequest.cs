public class ShareConversationRequest
{
    public Guid ConversationId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Name { get; set; }
    public Guid? ChatId { get; set; }
}