using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using AGUI.Abstractions;
using AGUI.Server;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Services.Agents;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/agui")]
[ApiController]
public class AgUiController : ControllerBase
{
    private readonly AgentService _agentService;
    private readonly AgentDbContext _dbContext;
    private readonly AgentAccessService _agentAccessService;
    private readonly ILogger<AgUiController> _logger;
    private readonly IMemoryCache _cache;
    private readonly IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> _jsonOptions;

    // Maps threadId → conversationId. Cached with a sliding expiration so the map cannot grow
    // unbounded; evicted entries are recovered from the DB lookup in GetOrCreateConversationAsync.
    // Avoids a DB schema change; acceptable for single-instance dev/staging.
    private const string ThreadConversationKeyPrefix = "agui:thread-conversation:";
    private static readonly MemoryCacheEntryOptions ThreadConversationEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromHours(4)
    };

    public AgUiController(
        AgentService agentService,
        AgentDbContext dbContext,
        AgentAccessService agentAccessService,
        ILogger<AgUiController> logger,
        IMemoryCache cache,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _agentService = agentService;
        _dbContext = dbContext;
        _agentAccessService = agentAccessService;
        _logger = logger;
        _cache = cache;
        _jsonOptions = jsonOptions;
    }

    [HttpPost("{agentId}")]
    public async Task RunAgentAsync(Guid agentId, [FromBody] RunAgentInput input)
    {
        var threadId = input.ThreadId;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            // Without a thread id every request would share the same conversation mapping.
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "threadId is required." });
            return;
        }
        var runId = string.IsNullOrWhiteSpace(input.RunId) ? NewId() : input.RunId;

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        Guid? userId = User.GetUserId();

        // Checked here rather than left to the run, for two reasons. It stops an inaccessible
        // agent creating a conversation row — GetOrCreateConversationAsync below writes one before
        // anything has established the caller may chat at all, so a signed-out client polling this
        // endpoint quietly accumulated rows. And it is the only place the refusal can be phrased
        // for a human: past this point the denial arrives as an exception from deep inside the
        // agent factory, which the catch-all downstream turns into "an internal error".
        //
        // Not a substitute for that catch. This mirrors AgentsController's check, and neither it
        // nor this one looks at AgentKind — AgentFactory.CreateAgent additionally refuses an inner,
        // tool-only agent, so the refusal can still surface from the run.
        if (!await _agentAccessService.HasAccessAsync(agentId, userId, User.IsInRole("Admin"), HttpContext.RequestAborted))
        {
            _logger.LogInformation(
                "AG-UI run refused for agent {AgentId} on thread {ThreadId}: caller has no access", agentId, threadId);

            await WriteEventAsync(new { type = "RUN_STARTED", threadId, runId, timestamp = Now() });
            await WriteEventAsync(new { type = "STEP_STARTED", stepName = "chat", timestamp = Now() });
            await WriteAssistantMessageAsync(AccessDeniedMessage);
            await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
            await WriteEventAsync(new { type = "RUN_FINISHED", threadId, runId, timestamp = Now() });
            return;
        }

        var runStarted = false;
        var stepStarted = false;
        var stepFinished = false;
        try
        {
            var conversationId = await GetOrCreateConversationAsync(userId, threadId);
            input.Messages = NormalizeMessagesForSdk(input.Messages);
            var requestContext = input.ToChatRequestContext(_jsonOptions.Value.SerializerOptions);
            var prompt = ExtractPrompt(requestContext.Input.Messages);
            var frontendToolsJson = BuildFrontendToolsJson(requestContext.Input.Tools);

            // Tool-result follow-up turns produce a synthetic acknowledgement prompt; don't persist
            // it as a user message (otherwise the instruction text shows up as a user message).
            var lastNonSystem = input.Messages.LastOrDefault(m => m.Role != "system" && m.Role != "developer");
            var isToolResultTurn = lastNonSystem?.Role == "tool";

            var promptRequest = new PromptRequestForm(
                Prompt: prompt,
                ConversationId: conversationId,
                SessionId: threadId,
                Documents: null,
                AgentId: agentId)
            {
                FrontendToolsJson = frontendToolsJson,
                PersistUserMessage = !isToolResultTurn
            };

            // NTG.Agent.CopilotKitApp: an AG-UI client with the A2UI renderer mounted. This is the one
            // endpoint where a rendered surface, a frontend tool call and a tool-render card all
            // have somewhere to land.
            var responses = NormalizeRunResponses(
                _agentService.ChatStreamingAsync(
                    userId, promptRequest, capabilities: ChatClientCapabilities.GenerativeUi),
                threadId,
                agentId);
            var updates = AgUiChatResponseAdapter.ToChatResponseUpdates(
                responses,
                HttpContext.RequestAborted);

            await foreach (var agUiEvent in updates.AsAGUIEventStreamAsync(
                requestContext,
                HttpContext.RequestAborted))
            {
                if (!stepStarted && agUiEvent is RunStartedEvent)
                {
                    await WriteEventAsync(agUiEvent);
                    await WriteEventAsync(new { type = "STEP_STARTED", stepName = "chat", timestamp = Now() });
                    stepStarted = true;
                    runStarted = true;
                    continue;
                }

                if (!stepFinished && agUiEvent is RunFinishedEvent or RunErrorEvent)
                {
                    await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
                    stepFinished = true;
                }

                await WriteEventAsync(agUiEvent);
            }

            if (!stepFinished)
                await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
        }
        catch (AnonymousRateLimitExceededException)
        {
            await WriteAssistantMessageAsync(
                "⚠️ You've reached the message limit for anonymous users. Please sign in to continue.");
            await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
        }
        catch (AgentAccessDeniedException)
        {
            // Reached only for a denial the up-front check does not make — an inner, tool-only
            // agent, or access revoked between that check and the run. Answered the same way, and
            // deliberately NOT as RUN_ERROR: being signed out is an ordinary outcome the user can
            // act on, and dressing it as an internal fault is what sent a debugging session after
            // a crash that never happened.
            _logger.LogInformation(
                "AG-UI run for thread {ThreadId} was refused by the agent factory: no access to {AgentId}",
                threadId, agentId);

            await WriteAssistantMessageAsync(AccessDeniedMessage);
            await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected mid-stream; nothing useful can be written back.
            _logger.LogDebug("AG-UI run for thread {ThreadId} was cancelled by client disconnect", threadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AG-UI agent run failed for thread {ThreadId}", threadId);
            if (!runStarted)
            {
                await WriteEventAsync(new { type = "RUN_STARTED", threadId, runId, timestamp = Now() });
                runStarted = true;
            }

            if (!stepStarted)
            {
                await WriteEventAsync(new { type = "STEP_STARTED", stepName = "chat", timestamp = Now() });
                stepStarted = true;
            }

            await WriteEventAsync(new { type = "STEP_FINISHED", stepName = "chat", timestamp = Now() });
            await WriteEventAsync(new { type = "RUN_ERROR", message = "An internal error occurred.", code = "INTERNAL_ERROR", timestamp = Now() });
        }
    }

    /// <summary>
    /// What the caller is told when the agent is not theirs to chat with. Worded for someone who is
    /// simply signed out, because that is overwhelmingly the reason: an agent is reachable by its
    /// owner, an Admin, or a role it has been granted, and a fresh install grants it to nobody.
    /// </summary>
    private const string AccessDeniedMessage =
        "You do not have access to this agent. Sign in with an account that can use it, or ask an "
        + "administrator to grant your role access.";

    /// <summary>One complete assistant message: start, one delta, end.</summary>
    private async Task WriteAssistantMessageAsync(string text)
    {
        var id = NewId();
        await WriteEventAsync(new { type = "TEXT_MESSAGE_START", messageId = id, role = "assistant", timestamp = Now() });
        await WriteEventAsync(new { type = "TEXT_MESSAGE_CONTENT", messageId = id, delta = text, timestamp = Now() });
        await WriteEventAsync(new { type = "TEXT_MESSAGE_END", messageId = id, timestamp = Now() });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private async Task WriteEventAsync(object data)
    {
        var json = data is BaseEvent
            ? JsonSerializer.Serialize(data, _jsonOptions.Value.SerializerOptions)
            : JsonSerializer.Serialize(data, _camelCase);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }

    private async Task<Guid> GetOrCreateConversationAsync(Guid? userId, string threadId)
    {
        var cacheKey = ThreadConversationKeyPrefix + threadId;
        if (_cache.TryGetValue(cacheKey, out Guid cached))
            return cached;

        // Try to find an existing conversation in DB for this thread
        Conversation? existing = null;
        if (userId.HasValue && Guid.TryParse(threadId, out var authedThreadGuid))
        {
            existing = await _dbContext.Conversations
                .FirstOrDefaultAsync(c => c.UserId == userId && c.SessionId == authedThreadGuid);
        }
        else if (!userId.HasValue && Guid.TryParse(threadId, out var threadGuid))
        {
            existing = await _dbContext.Conversations
                .FirstOrDefaultAsync(c => c.SessionId == threadGuid && c.UserId == null);
        }

        if (existing != null)
        {
            _cache.Set(cacheKey, existing.Id, ThreadConversationEntryOptions);
            return existing.Id;
        }

        // Create a new conversation
        var sessionId = Guid.TryParse(threadId, out var sg) ? sg : (Guid?)null;
        var conversation = new Conversation
        {
            Name = "New Conversation",
            UserId = userId,
            SessionId = sessionId
        };
        _dbContext.Conversations.Add(conversation);
        await _dbContext.SaveChangesAsync();

        _cache.Set(cacheKey, conversation.Id, ThreadConversationEntryOptions);
        return conversation.Id;
    }

    private async IAsyncEnumerable<PromptResponse> NormalizeRunResponses(
        IAsyncEnumerable<PromptResponse> responses,
        string threadId,
        Guid agentId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = responses
            .WithCancellation(cancellationToken)
            .GetAsyncEnumerator();

        PromptResponse? fallback = null;
        while (true)
        {
            var hasNext = false;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (AnonymousRateLimitExceededException)
            {
                fallback = new PromptResponse(
                    "⚠️ You've reached the message limit for anonymous users. Please sign in to continue.");
                break;
            }
            catch (AgentAccessDeniedException)
            {
                _logger.LogInformation(
                    "AG-UI run for thread {ThreadId} was refused by the agent factory: no access to {AgentId}",
                    threadId,
                    agentId);
                fallback = new PromptResponse(AccessDeniedMessage);
                break;
            }

            if (!hasNext)
                break;

            yield return enumerator.Current;
        }

        if (fallback is not null)
            yield return fallback;
    }

    /// <summary>
    /// Extracts the prompt from the message list.
    /// For a normal user turn: returns the last user message content.
    /// For a tool-result follow-up turn: builds a synthetic acknowledgement prompt.
    /// </summary>
    private static string ExtractPrompt(IEnumerable<AGUIMessage> messages)
    {
        // Check if last non-system message is a tool result
        var lastNonSystem = messages.LastOrDefault(m => m is not AGUISystemMessage and not AGUIDeveloperMessage);
        if (lastNonSystem is AGUIToolMessage toolMessage)
        {
            // Find the tool name from the matching assistant tool call
            var toolCallId = toolMessage.ToolCallId ?? "";
            var toolName = messages
                .OfType<AGUIAssistantMessage>()
                .Where(m => m.ToolCalls != null)
                .Select(m => m.ToolCalls!.FirstOrDefault(t => t.Id == toolCallId))
                .Where(match => match != null)
                .Select(match => match!.Function?.Name)
                .FirstOrDefault(name => !string.IsNullOrEmpty(name))
                ?? "unknown_tool";
            var resultText = toolMessage.Content.ToString();

            // A submitted surface is not an approval, and must not be prompted like one.
            //
            // The acknowledgement wording below was written for human-in-the-loop tools, where the
            // right response really is "confirm what changed and don't call the tool again". Applied
            // to a surface submission it does the opposite of what is needed: it asks for a
            // text-only confirmation and discourages rendering the next step. And because this text
            // lands in the *user* turn — the most recent, highest-salience position — it outranks
            // any system-message guidance telling the model to continue a multi-step flow.
            //
            // Observed: step 2 of the travel skill survived (a search submission has no "approval"
            // reading), while step 3 did not — "the user picked option 2" maps exactly onto "if
            // approved, briefly confirm what changed", so the model confirmed in prose and stopped,
            // one surface short of finishing. Sending the identical text as an ordinary user message
            // rendered the final surface correctly, which is what isolated this to the prompt rather
            // than to the model or the skill.
            if (string.Equals(toolName, A2uiPrompt.EventToolName, StringComparison.OrdinalIgnoreCase))
            {
                return $"[The user submitted a rendered surface. The event was: {resultText}] " +
                    "Read the values they submitted and continue from wherever this leaves the task. " +
                    "If you are following a skill, re-read its instructions first and carry out the " +
                    "next step it defines — including rendering the next surface, if it defines one. " +
                    "Do not simply restate what they chose.";
            }

            return $"[The user responded to the \"{toolName}\" request with: {resultText}] " +
                "Acknowledge the outcome appropriately: if approved, briefly confirm what changed; " +
                "if denied or different from what you proposed, ask what they'd like instead. " +
                "Do not call the tool again with the same arguments without checking first.";
        }

        // Normal user turn: last user message
        var lastUser = messages.OfType<AGUIUserMessage>().LastOrDefault();
        return lastUser?.Content.ToString() ?? "";
    }

    private static string? BuildFrontendToolsJson(IEnumerable<AGUITool>? tools)
    {
        if (tools is null) return null;

        var toolList = tools.ToList();
        if (toolList.Count == 0) return null;

        var items = toolList.Select(t => new
        {
            name = t.Name,
            description = t.Description ?? "",
            parameters = t.Parameters
        });

        return JsonSerializer.Serialize(items, _camelCase);
    }

    private List<AGUIMessage> NormalizeMessagesForSdk(IList<AGUIMessage> messages)
    {
        var normalized = messages
            .Where(message => !string.Equals(message.Role, "reasoning", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removedCount = messages.Count - normalized.Count;
        if (removedCount > 0)
        {
            _logger.LogDebug(
                "Removed {Count} non-protocol reasoning message(s) before AG-UI SDK request conversion",
                removedCount);
        }

        return normalized;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string NewId() => Guid.NewGuid().ToString();
}
