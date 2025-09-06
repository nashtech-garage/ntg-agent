using NTG.Agent.Shared.Dtos.Upload;

namespace NTG.Agent.Shared.Dtos.Chats;

public record PromptRequest
    (string Prompt,
    Guid ConversationId,
    string? SessionId,
    IEnumerable<UploadItemContent> Documents);