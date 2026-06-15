using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Services.Agents;
using System.Collections.Concurrent;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/agui")]
[ApiController]
public class AgUiController : ControllerBase
{
    private readonly AgentService _agentService;
    private readonly AgentDbContext _dbContext;
    private readonly ILogger<AgUiController> _logger;

    // Maps threadId → conversationId for the lifetime of the server process.
    // Avoids a DB schema change; acceptable for single-instance dev/staging.
    private static readonly ConcurrentDictionary<string, Guid> _threadConversationMap = new();

    public AgUiController(AgentService agentService, AgentDbContext dbContext, ILogger<AgUiController> logger)
    {
        _agentService = agentService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost("{agentId}")]
    public async Task RunAgentAsync(Guid agentId, [FromBody] AgUiRunRequest input)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var threadId = input.ThreadId;
        var runId = input.RunId;
        Guid? userId = User.GetUserId();

        var conversationId = await GetOrCreateConversationAsync(userId, threadId);

        var prompt = ExtractPrompt(input.Messages);
        var frontendToolsJson = BuildFrontendToolsJson(input.Tools);

        var promptRequest = new PromptRequestForm(
            Prompt: prompt,
            ConversationId: conversationId,
            SessionId: threadId,
            Documents: null,
            AgentId: agentId)
        {
            FrontendToolsJson = frontendToolsJson
        };

        await WriteEventAsync(new { type = "RUN_STARTED", threadId, runId, timestamp = Now() });
        await WriteEventAsync(new { type = "STEP_STARTED", stepName = "chat", timestamp = Now() });

        var messageId = NewId();
        var textOpen = false;
        var reasoningId = NewId();
        var reasoningOpen = false;

        try
        {
            await foreach (var chunk in _agentService.ChatStreamingAsync(userId, promptRequest))
            {
                if (chunk.ContentType == PromptContentType.Thinking && !string.IsNullOrEmpty(chunk.Content))
                {
                    // Close any open text block before reasoning starts
                    if (textOpen)
                    {
                        await WriteEventAsync(new { type = "TEXT_MESSAGE_END", messageId, timestamp = Now() });
                        textOpen = false;
                        messageId = NewId();
                    }

                    if (!reasoningOpen)
                    {
                        await WriteEventAsync(new { type = "REASONING_START", messageId = reasoningId, timestamp = Now() });
                        await WriteEventAsync(new { type = "REASONING_MESSAGE_START", messageId = reasoningId, role = "reasoning", timestamp = Now() });
                        reasoningOpen = true;
                    }
                    await WriteEventAsync(new { type = "REASONING_MESSAGE_CONTENT", messageId = reasoningId, delta = chunk.Content, timestamp = Now() });
                }
                else if (chunk.ContentType == PromptContentType.ToolCall && !string.IsNullOrEmpty(chunk.Content))
                {
                    // Close reasoning and text before a tool call
                    if (reasoningOpen)
                    {
                        await WriteEventAsync(new { type = "REASONING_MESSAGE_END", messageId = reasoningId, timestamp = Now() });
                        await WriteEventAsync(new { type = "REASONING_END", messageId = reasoningId, timestamp = Now() });
                        reasoningOpen = false;
                        reasoningId = NewId();
                    }
                    if (textOpen)
                    {
                        await WriteEventAsync(new { type = "TEXT_MESSAGE_END", messageId, timestamp = Now() });
                        textOpen = false;
                        messageId = NewId();
                    }

                    JsonElement toolCall;
                    try { toolCall = JsonDocument.Parse(chunk.Content).RootElement; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse tool call chunk");
                        continue;
                    }

                    var toolCallId = toolCall.TryGetProperty("callId", out var cid) ? cid.GetString() ?? NewId() : NewId();
                    var toolName = toolCall.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";
                    var argsEl = toolCall.TryGetProperty("arguments", out var a) ? a : default;
                    var args = argsEl.ValueKind != JsonValueKind.Undefined ? JsonSerializer.Serialize(argsEl) : "{}";

                    await WriteEventAsync(new { type = "TOOL_CALL_START", toolCallId, toolCallName = toolName, timestamp = Now() });
                    await WriteEventAsync(new { type = "TOOL_CALL_ARGS", toolCallId, delta = args, timestamp = Now() });
                    await WriteEventAsync(new { type = "TOOL_CALL_END", toolCallId, timestamp = Now() });
                }
                else if (chunk.ContentType == PromptContentType.Text && !string.IsNullOrEmpty(chunk.Content))
                {
                    // Close reasoning block when text starts
                    if (reasoningOpen)
                    {
                        await WriteEventAsync(new { type = "REASONING_MESSAGE_END", messageId = reasoningId, timestamp = Now() });
                        await WriteEventAsync(new { type = "REASONING_END", messageId = reasoningId, timestamp = Now() });
                        reasoningOpen = false;
                        reasoningId = NewId();
                    }

                    if (!textOpen)
                    {
                        await WriteEventAsync(new { type = "TEXT_MESSAGE_START", messageId, role = "assistant", timestamp = Now() });
                        textOpen = true;
                    }
                    await WriteEventAsync(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = chunk.Content, timestamp = Now() });
                }
            }

            if (reasoningOpen)
            {
                await WriteEventAsync(new { type = "REASONING_MESSAGE_END", messageId = reasoningId, timestamp = Now() });
                await WriteEventAsync(new { type = "REASONING_END", messageId = reasoningId, timestamp = Now() });
            }
            if (textOpen)
                await WriteEventAsync(new { type = "TEXT_MESSAGE_END", messageId, timestamp = Now() });

            await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
            await WriteEventAsync(new { type = "RUN_FINISHED", threadId, runId, timestamp = Now() });
        }
        catch (AnonymousRateLimitExceededException)
        {
            if (reasoningOpen)
            {
                await WriteEventAsync(new { type = "REASONING_MESSAGE_END", messageId = reasoningId, timestamp = Now() });
                await WriteEventAsync(new { type = "REASONING_END", messageId = reasoningId, timestamp = Now() });
            }
            if (textOpen)
                await WriteEventAsync(new { type = "TEXT_MESSAGE_END", messageId, timestamp = Now() });

            var rateMsgId = NewId();
            await WriteEventAsync(new { type = "TEXT_MESSAGE_START", messageId = rateMsgId, role = "assistant", timestamp = Now() });
            await WriteEventAsync(new { type = "TEXT_MESSAGE_CONTENT", messageId = rateMsgId, delta = "⚠️ You've reached the message limit for anonymous users. Please sign in to continue.", timestamp = Now() });
            await WriteEventAsync(new { type = "TEXT_MESSAGE_END", messageId = rateMsgId, timestamp = Now() });
            await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
            await WriteEventAsync(new { type = "RUN_FINISHED", threadId, runId, timestamp = Now() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AG-UI agent run failed for thread {ThreadId}", threadId);
            if (reasoningOpen)
            {
                await WriteEventAsync(new { type = "REASONING_MESSAGE_END", messageId = reasoningId, timestamp = Now() });
                await WriteEventAsync(new { type = "REASONING_END", messageId = reasoningId, timestamp = Now() });
            }
            if (textOpen)
                await WriteEventAsync(new { type = "TEXT_MESSAGE_END", messageId, timestamp = Now() });
            await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
            await WriteEventAsync(new { type = "RUN_ERROR", message = "An internal error occurred.", code = "INTERNAL_ERROR", timestamp = Now() });
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private async Task WriteEventAsync(object data)
    {
        var json = JsonSerializer.Serialize(data, _camelCase);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }

    private async Task<Guid> GetOrCreateConversationAsync(Guid? userId, string threadId)
    {
        if (_threadConversationMap.TryGetValue(threadId, out var cached))
            return cached;

        // Try to find an existing conversation in DB for this thread
        Conversation? existing = null;
        if (userId.HasValue)
        {
            existing = await _dbContext.Conversations
                .FirstOrDefaultAsync(c => c.UserId == userId && c.SessionId == Guid.Parse(threadId));
        }
        else if (Guid.TryParse(threadId, out var threadGuid))
        {
            existing = await _dbContext.Conversations
                .FirstOrDefaultAsync(c => c.SessionId == threadGuid && c.UserId == null);
        }

        if (existing != null)
        {
            _threadConversationMap[threadId] = existing.Id;
            return existing.Id;
        }

        // Create a new conversation
        var sessionId = Guid.TryParse(threadId, out var sg) ? sg : (Guid?)null;
        var conversation = new Conversation
        {
            Name = "New Conversation",
            UserId = userId,
            SessionId = userId.HasValue ? sessionId : sessionId
        };
        _dbContext.Conversations.Add(conversation);
        await _dbContext.SaveChangesAsync();

        _threadConversationMap[threadId] = conversation.Id;
        return conversation.Id;
    }

    /// <summary>
    /// Extracts the prompt from the message list.
    /// For a normal user turn: returns the last user message content.
    /// For a tool-result follow-up turn: builds a synthetic acknowledgement prompt.
    /// </summary>
    private static string ExtractPrompt(List<AgUiMessage> messages)
    {
        // Check if last non-system message is a tool result
        var lastNonSystem = messages.LastOrDefault(m => m.Role != "system" && m.Role != "developer");
        if (lastNonSystem?.Role == "tool")
        {
            // Find the tool name from the matching assistant tool call
            var toolCallId = lastNonSystem.ToolCallId ?? "";
            var toolName = "unknown_tool";
            foreach (var m in messages)
            {
                if (m.Role == "assistant" && m.ToolCalls != null)
                {
                    var match = m.ToolCalls.FirstOrDefault(t => t.Id == toolCallId);
                    if (match != null)
                    {
                        toolName = match.Function?.Name ?? toolName;
                        break;
                    }
                }
            }
            var resultText = lastNonSystem.Content ?? "";
            return $"[The browser tool \"{toolName}\" was executed and returned: {resultText}] Briefly confirm to the user what was done. Do not call the tool again.";
        }

        // Normal user turn: last user message
        var lastUser = messages.LastOrDefault(m => m.Role == "user");
        return lastUser?.Content ?? "";
    }

    private static string? BuildFrontendToolsJson(List<AgUiTool>? tools)
    {
        if (tools == null || tools.Count == 0) return null;

        var items = tools.Select(t => new
        {
            name = t.Name,
            description = t.Description ?? "",
            parameters = t.Parameters
        });

        return JsonSerializer.Serialize(items, _camelCase);
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string NewId() => Guid.NewGuid().ToString();
}
