using Microsoft.AspNetCore.Components.Forms;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Common.Dtos.Upload;

namespace NTG.Agent.Orchestrator.Dtos;

public class UploadItemForm : UploadItem
{
    public IFormFile? Content { get; set; }
}

public record PromptRequestForm(string Prompt, Guid ConversationId, string? SessionId, IEnumerable<UploadItemForm>? Documents, Guid AgentId) : PromptRequest<UploadItemForm>(Prompt, ConversationId, SessionId, Documents, AgentId);