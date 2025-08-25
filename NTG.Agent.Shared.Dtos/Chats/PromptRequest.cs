namespace NTG.Agent.Shared.Dtos.Chats;

public record PromptRequest
    (string Prompt,
    Guid ConversationId,
    string? SessionId,
    string? ImageBase64,
    string? ImageContentType);