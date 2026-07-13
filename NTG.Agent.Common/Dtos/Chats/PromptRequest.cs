using NTG.Agent.Common.Dtos.Upload;

namespace NTG.Agent.Common.Dtos.Chats;

public record PromptRequest<TUpload>(
    string Prompt,
    Guid ConversationId,
    string? SessionId,
    IEnumerable<TUpload>? Documents,
    Guid AgentId,
    string? SkillName = null
)
where TUpload : UploadItem;