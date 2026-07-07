using Microsoft.AspNetCore.Components.Forms;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Common.Dtos.Upload;

namespace NTG.Agent.Orchestrator.Dtos;

public class UploadItemForm : UploadItem
{
    public IFormFile? Content { get; set; }
}

public record PromptRequestForm(string Prompt, Guid ConversationId, string? SessionId, IEnumerable<UploadItemForm>? Documents, Guid AgentId) : PromptRequest<UploadItemForm>(Prompt, ConversationId, SessionId, Documents, AgentId)
{
    /// <summary>
    /// Optional JSON array of AG-UI frontend tool definitions ({name, description, parameters})
    /// supplied by the CopilotKit client. These are declared to the LLM but executed in the browser.
    /// </summary>
    public string? FrontendToolsJson { get; set; }

    /// <summary>
    /// Whether to persist the prompt as a user message in the conversation history.
    /// Set to <c>false</c> for tool-result follow-up turns, whose <see cref="PromptRequest{TUpload}.Prompt"/>
    /// is a synthetic acknowledgement instruction (not something the user typed) and should not
    /// appear in the chat transcript.
    /// </summary>
    public bool PersistUserMessage { get; init; } = true;
}