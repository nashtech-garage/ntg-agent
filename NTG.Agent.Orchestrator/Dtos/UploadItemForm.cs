﻿using Microsoft.AspNetCore.Components.Forms;
using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Upload;

namespace NTG.Agent.Orchestrator.Dtos;

public class UploadItemForm : UploadItem
{
    public IFormFile? Content { get; set; }
}

public record PromptRequestForm(string Prompt, Guid ConversationId, string? SessionId, IEnumerable<UploadItemForm>? Documents) : PromptRequest<UploadItemForm>(Prompt, ConversationId, SessionId, Documents);