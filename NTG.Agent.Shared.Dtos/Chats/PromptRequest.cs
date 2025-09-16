using NTG.Agent.Shared.Dtos.Upload;

namespace NTG.Agent.Shared.Dtos.Chats;

public record PromptRequest<TUpload>(
    string Prompt,
    Guid ConversationId,
    string? SessionId,
    IEnumerable<TUpload>? Documents
)
where TUpload : UploadItem;