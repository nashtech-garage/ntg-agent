public class ShareConversationRequest
{
    public DateTime? ExpiresAt { get; set; }
    public string? Note { get; set; } = "Shared conversation";
}