namespace NTG.Agent.Shared.Dtos.Chats;

public class ChatMessageItem
{
    public Guid Id { get; set; }
    public bool IsSystem { get; set; }
    public string Message { get; set; } = string.Empty;
    public ReactionType Reaction { get; set; }
    public string UserComment { get; set; } = string.Empty;

    public string? ImageBase64 { get; set; }
    public string? ImageContentType { get; set; }
}
