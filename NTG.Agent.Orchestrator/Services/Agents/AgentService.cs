using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Common.Dtos.Constants;
using NTG.Agent.Common.Dtos.TokenUsage;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Models.TokenUsage;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.AnonymousSessions;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;
using NTG.Agent.Orchestrator.Services.Skills;
using NTG.Agent.Common.Knowledge;
using System.Text;
using System.Text.Json;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

namespace NTG.Agent.Orchestrator.Services.Agents;

public class AgentService
{
    private readonly IAgentFactory _agentFactory;
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IAnonymousSessionService _anonymousSessionService;
    private readonly IIpAddressService _ipAddressService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDocumentAnalysisService _documentAnalysisService;
    private readonly AgentAccessService _agentAccessService;
    private readonly RenderableToolCapture _renderableToolCapture;
    private readonly SkillRegistry _skillRegistry;
    private readonly SkillActivityLog _skillActivityLog;
    private readonly ILogger<AgentService> _logger;
    private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

    public AgentService(
        IAgentFactory agentFactory,
        AgentDbContext agentDbContext,
        IKnowledgeService knowledgeService,
        IAnonymousSessionService anonymousSessionService,
        IIpAddressService ipAddressService,
        IHttpContextAccessor httpContextAccessor,
        IDocumentAnalysisService documentAnalysisService,
        AgentAccessService agentAccessService,
        RenderableToolCapture renderableToolCapture,
        SkillRegistry skillRegistry,
        SkillActivityLog skillActivityLog,
        ILogger<AgentService> logger)
    {
        _agentFactory = agentFactory;
        _agentDbContext = agentDbContext;
        _knowledgeService = knowledgeService;
        _anonymousSessionService = anonymousSessionService;
        _ipAddressService = ipAddressService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _documentAnalysisService = documentAnalysisService;
        _agentAccessService = agentAccessService;
        _renderableToolCapture = renderableToolCapture;
        _skillRegistry = skillRegistry;
        _skillActivityLog = skillActivityLog;
    }

    // Turns tool results captured during the run (get_weather, possibly inside an inner agent)
    // into ToolCall + ToolResult chunks the AG-UI controller forwards to the browser to render.
    //
    // Only a client that can render them gets them. Nothing in the Blazor chat renders a ToolCall
    // or a ToolResult — its stream loop appends every non-Thinking chunk straight into the
    // assistant's bubble — so forwarding them there pastes raw protocol JSON into the reply. The
    // buffer is emptied either way: the tool did run, and a capture left queued would surface later
    // against some other part of the stream.
    //
    // Withholding them also stops the Tool-role row being written for that conversation, since the
    // envelope SaveMessages persists is built from these very chunks. That is the intended shape —
    // the row exists so an A2UI client can rehydrate a card on reload, and a text-only client has
    // no card to rehydrate — but it does mean such a conversation stores no record of the call.
    private IEnumerable<PromptResponse> DrainRenderableToolCalls(ChatClientCapabilities capabilities)
    {
        if (capabilities != ChatClientCapabilities.GenerativeUi)
        {
            // Eager, not a lazy iterator that yields nothing. In this branch emptying the buffer is
            // the method's only effect, and an effect that happens only if the caller remembers to
            // enumerate the result is one a refactor can drop without a single warning.
            _renderableToolCapture.DiscardPending();
            return [];
        }

        return ForwardRenderableToolCalls();
    }

    private IEnumerable<PromptResponse> ForwardRenderableToolCalls()
    {
        foreach (var captured in _renderableToolCapture.DrainPending())
        {
            yield return new PromptResponse(
                JsonSerializer.Serialize(new { callId = captured.CallId, name = captured.Name, arguments = captured.Arguments }),
                PromptContentType.ToolCall);

            yield return new PromptResponse(
                JsonSerializer.Serialize(new { callId = captured.CallId, result = captured.Result }),
                PromptContentType.ToolResult);
        }
    }

    // Turns skill narration captured during the run (a skill loading, a surface rendering,
    // possibly inside an inner agent) into SkillNotice chunks the AG-UI controller forwards to the
    // browser as reasoning content, so the "Thought for N seconds" panel says which skill acted.
    //
    // Gated on the same terms as the tool-render chunks above, though nothing writes to the log on
    // a text-only run today: the only two writers are the skill tools, and those are not attached.
    // The gate is here because that reasoning is a chain through three files, and the buffer is
    // deliberately shared with inner agents — the moment a skill tool is baked in by AgentFactory
    // rather than attached per request, the chain breaks and this is where it would leak.
    private IEnumerable<PromptResponse> DrainSkillNotices(ChatClientCapabilities capabilities)
    {
        if (capabilities != ChatClientCapabilities.GenerativeUi)
        {
            _skillActivityLog.DiscardPending();
            return [];
        }

        return ForwardSkillNotices();
    }

    private IEnumerable<PromptResponse> ForwardSkillNotices()
    {
        foreach (var line in _skillActivityLog.DrainPending())
        {
            yield return new PromptResponse(line, PromptContentType.SkillNotice);
        }
    }

    /// <param name="capabilities">
    /// What the calling client can render, stated by the controller that received the request.
    /// Defaults to <see cref="ChatClientCapabilities.TextOnly"/> so a caller that does not say gets
    /// the plain-text run rather than chunks it cannot display.
    /// </param>
    public async IAsyncEnumerable<PromptResponse> ChatStreamingAsync(
        Guid? userId,
        PromptRequestForm promptRequest,
        bool isAdmin = false,
        ChatClientCapabilities capabilities = ChatClientCapabilities.TextOnly)
    {
        var startTime = DateTime.UtcNow;
        var anonymousSessionId = Guid.Empty;
        string? anonymousIpAddress = null;

        // Rate limit check for anonymous users
        if (!userId.HasValue)
        {
            if (!Guid.TryParse(promptRequest.SessionId, out anonymousSessionId))
            {
                throw new InvalidOperationException("A valid Session ID is required for unauthenticated requests.");
            }

            var httpContext = _httpContextAccessor.HttpContext;
            anonymousIpAddress = httpContext != null ? _ipAddressService.GetClientIpAddress(httpContext) : null;
            
            var rateLimitStatus = await _anonymousSessionService.CheckRateLimitAsync(anonymousSessionId, anonymousIpAddress);
            
            if (!rateLimitStatus.CanSendMessage)
            {
                throw new AnonymousRateLimitExceededException(
                    "You've reached the message limit for anonymous users. Please sign in to continue.",
                    rateLimitStatus);
            }
        }
        
        var conversation = await ValidateConversation(userId, promptRequest);
        var history = await PrepareConversationHistory(userId, promptRequest.SessionId, promptRequest.AgentId, conversation);
        var tags = await GetUserTags(userId);
        var ocrDocuments = new List<string>();
        if (_documentAnalysisService.IsEnabled && promptRequest.Documents is not null && promptRequest.Documents.Any())
        {
            ocrDocuments = await _documentAnalysisService.ExtractDocumentData(promptRequest.Documents);
        }

        // Naming is cosmetic and runs before the reply, so a failure here must not take the whole
        // run with it. Unguarded, an unreachable provider surfaced to the user as a bare
        // INTERNAL_ERROR with no answer at all — and only on the first message of a conversation,
        // which made it look like an intermittent chat fault rather than a configuration one.
        if (conversation.Name == "New Conversation")
        {
            try
            {
                var nameTokenUsage = new TokenUsageInfo();
                var nameStart = DateTime.UtcNow;
                conversation.Name = await GenerateConversationName(promptRequest.Prompt, nameTokenUsage);
                _agentDbContext.Conversations.Update(conversation);
                await _agentDbContext.SaveChangesAsync();
                await TrackTokenUsageAsync(userId, promptRequest.SessionId, promptRequest.AgentId, new ConversationListItem(conversation.Id, conversation.Name), null, OperationTypes.GenerateName, nameTokenUsage, DateTime.UtcNow - nameStart);
            }
            catch (Exception ex)
            {
                // Left as "New Conversation"; the user can rename it, and the chat proceeds.
                _logger.LogWarning(ex, "Could not generate a name for conversation {ConversationId}", conversation.Id);
            }
        }

        // Track text and thinking content separately — thinking is persisted but excluded from AI history
        var agentMessageSb = new StringBuilder();
        var thinkingMessageSb = new StringBuilder();
        var tokenUsageInfo = new TokenUsageInfo();
        // Track when the thinking phase begins and ends so we can persist the duration
        DateTime? thinkingStartedAt = null;
        DateTime? thinkingEndedAt = null;
        // Whether the PROVIDER produced reasoning, as opposed to the thinking buffer merely being
        // non-empty. The two are the same today, and this exists so they can stop being the same:
        // anything we write into that buffer ourselves — a narration of which skill was loaded,
        // say — would otherwise bill the run as OperationTypes.Reasoning on a model that never
        // reasoned. Keeping the billing signal separate from the display buffer is correct
        // regardless of what is eventually appended to it.
        var hasProviderThinking = false;
        // Accumulate renderable tool calls (e.g. get_weather) so the card can be rehydrated when the
        // conversation is reloaded. Pair each ToolCall with its later ToolResult by call id.
        var pendingToolCalls = new Dictionary<string, (string Name, string Arguments)>();
        var toolRenderEnvelopes = new List<object>();

        await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest, history, tags, ocrDocuments, tokenUsageInfo, userId, isAdmin, capabilities))
        {
            if (item.ContentType == PromptContentType.Thinking)
            {
                // Record the start timestamp on the first thinking chunk
                thinkingStartedAt ??= DateTime.UtcNow;
                thinkingMessageSb.Append(item.Content);
                hasProviderThinking = true;
            }
            else if (item.ContentType == PromptContentType.SkillNotice)
            {
                // Our own narration, not the provider's. It shares the thinking buffer so it
                // persists and reappears after a page reload, and it starts the duration clock the
                // same way provider thinking does — but it must never set hasProviderThinking,
                // since that flag alone decides Reasoning-vs-Chat billing.
                thinkingStartedAt ??= DateTime.UtcNow;
                thinkingMessageSb.Append(item.Content);
            }
            else if (item.ContentType == PromptContentType.ToolCall)
            {
                // Protocol payload, not persisted text. Remember the call so we can pair it with its result.
                CollectToolCall(item.Content, pendingToolCalls);
            }
            else if (item.ContentType == PromptContentType.ToolResult)
            {
                var envelope = BuildToolRenderEnvelope(item.Content, pendingToolCalls);
                if (envelope is not null) toolRenderEnvelopes.Add(envelope);
            }
            else
            {
                // Record the end timestamp on the first non-thinking chunk after thinking started
                if (thinkingStartedAt.HasValue && !thinkingEndedAt.HasValue)
                    thinkingEndedAt = DateTime.UtcNow;
                agentMessageSb.Append(item.Content);
            }

            yield return item;
        }

        var toolRenderJson = toolRenderEnvelopes.Count > 0 ? JsonSerializer.Serialize(toolRenderEnvelopes) : null;

        var responseTime = DateTime.UtcNow - startTime;
        // Calculate thinking duration; falls back to end-of-stream if no non-thinking chunk followed
        int? thinkingDurationMs = thinkingStartedAt.HasValue
            ? (int)((thinkingEndedAt ?? DateTime.UtcNow) - thinkingStartedAt.Value).TotalMilliseconds
            : null;

        try
        {
            var savedMessage = await SaveMessages(
                userId, promptRequest, conversation, capabilities,
                agentMessageSb.ToString(),
                thinkingMessageSb.Length > 0 ? thinkingMessageSb.ToString() : null,
                thinkingDurationMs,
                ocrDocuments,
                toolRenderJson);

            // Increment anonymous session counter after successful message
            if (!userId.HasValue)
            {
                await _anonymousSessionService.IncrementMessageCountAsync(anonymousSessionId, anonymousIpAddress);
            }

            // Use the Reasoning operation type when the model produced reasoning/thinking tokens.
            // For OpenAI, ReasoningTokens is populated from UsageDetails.ReasoningTokenCount.
            // For Anthropic, the SDK folds thinking tokens into OutputTokenCount and never sets
            // ReasoningTokenCount, so we fall back to checking for thinking content in the stream.
            var hasThinking = tokenUsageInfo.ReasoningTokens > 0 || hasProviderThinking;
            var chatOperationType = hasThinking ? OperationTypes.Reasoning : OperationTypes.Chat;
            await TrackTokenUsageAsync(userId, promptRequest.SessionId, promptRequest.AgentId, new ConversationListItem(conversation.Id, conversation.Name), savedMessage.Id, chatOperationType, tokenUsageInfo, responseTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save messages or post-process after streaming for conversation {ConversationId}", promptRequest.ConversationId);
        }
    }

    private async Task<Conversation> ValidateConversation(Guid? userId, PromptRequestForm promptRequest)
    {
        var conversationId = promptRequest.ConversationId;
        Conversation? conversation;

        if (userId.HasValue)
        {
            conversation = await _agentDbContext.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);
        }
        else
        {
            if (!Guid.TryParse(promptRequest.SessionId, out var sessionId))
                throw new InvalidOperationException("A valid Session ID is required for unauthenticated requests.");

            conversation = await _agentDbContext.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.SessionId == sessionId);
        }

        return conversation ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");
    }

    private async Task<List<string>> GetUserTags(Guid? userId)
    {
        if (userId is not null)
        {
            var roleIds = await _agentDbContext.UserRoles
                .Where(c => c.UserId == userId).Select(c => c.RoleId).ToListAsync();

            return await _agentDbContext.TagRoles
                .Where(c => roleIds.Contains(c.RoleId))
                .Select(c => c.TagId.ToString())
                .ToListAsync();
        }
        else
        {
            var anonymousRoleId = new Guid(Constants.AnonymousRoleId);
            return await _agentDbContext.TagRoles
                .Where(c => c.RoleId == anonymousRoleId)
                .Select(c => c.TagId.ToString())
                .ToListAsync();
        }
    }

    private async Task<List<PChatMessage>> PrepareConversationHistory(Guid? userId, string? sessionId, Guid agentId, Conversation conversation)
    {
        var historyMessages = await _agentDbContext.ChatMessages
            // Tool-role messages are UI-render payloads (e.g. the weather card), not part of the chat the
            // model should see — excluding them keeps the history free of orphan tool results.
            .Where(m => m.ConversationId == conversation.Id && m.Role != ChatRole.Tool)
            .OrderBy(m => m.UpdatedAt)
            .ToListAsync();

        if (historyMessages.Count <= MAX_LATEST_MESSAGE_TO_KEEP_FULL) return historyMessages;

        var toSummarize = historyMessages.Take(historyMessages.Count - MAX_LATEST_MESSAGE_TO_KEEP_FULL).ToList();
        var tokenUsageInfo = new TokenUsageInfo();
        var startTime = DateTime.UtcNow;
        var summary = await SummarizeMessagesAsync(toSummarize, tokenUsageInfo);
        var responseTime = DateTime.UtcNow - startTime;
        var summaryMsg = historyMessages.FirstOrDefault(m => m.IsSummary) ?? new PChatMessage
        {
            UserId = userId,
            Conversation = conversation,
            Role = ChatRole.System,
            IsSummary = true
        };

        summaryMsg.Content = $"Summary of earlier conversation: {summary}";
        summaryMsg.UpdatedAt = DateTime.UtcNow;

        _agentDbContext.Update(summaryMsg);

        await TrackTokenUsageAsync(userId, sessionId, agentId, new ConversationListItem(conversation.Id, conversation.Name), null, OperationTypes.SummarizeMessages, tokenUsageInfo, responseTime);

        return new List<PChatMessage> { summaryMsg }
            .Concat(historyMessages.TakeLast(MAX_LATEST_MESSAGE_TO_KEEP_FULL))
            .ToList();
    }

    private async Task<PChatMessage> SaveMessages(Guid? userId, PromptRequestForm promptRequest, Conversation conversation, ChatClientCapabilities capabilities, string assistantReply, string? thinkingContent, int? thinkingDurationMs, List<string> ocrDocuments, string? toolRenderJson = null)
    {
        // Note: conversation name generation was moved to before streaming in ChatStreamingAsync.
        // Stamp explicit, strictly increasing timestamps so reload order is deterministic:
        // user question → tool render (weather card) → assistant text reply.
        // UpdatedAt is aligned with CreatedAt because PrepareConversationHistory orders by UpdatedAt;
        // leaving it at the constructor timestamp would let the assistant sort before the user message.
        var now = DateTime.UtcNow;
        var assistantMessage = new PChatMessage
        {
            UserId = userId,
            Conversation = conversation,
            Content = assistantReply,
            ThinkingContent = thinkingContent,
            ThinkingDurationMs = thinkingDurationMs,
            Role = ChatRole.Assistant,
            CreatedAt = now.AddMilliseconds(2),
            UpdatedAt = now.AddMilliseconds(2)
        };

        // Tool-result follow-up turns carry a synthetic acknowledgement prompt that the user
        // never typed — persist only the assistant reply so it doesn't pollute the transcript.
        //
        // Honoured only for the client that has such turns. They exist solely on the AG-UI path,
        // and PromptRequestForm is [FromForm]-bound on the other one — so on a text-only run the
        // flag is caller-supplied with no legitimate sender, and a crafted post would run the
        // agent, spend the tokens and keep the reply while quietly dropping the user's own message
        // from the transcript.
        var persistUserMessage = promptRequest.PersistUserMessage
            || capabilities != ChatClientCapabilities.GenerativeUi;

        if (persistUserMessage)
        {
            var userMessage = new PChatMessage { UserId = userId, Conversation = conversation, Content = promptRequest.Prompt, Role = ChatRole.User, CreatedAt = now, UpdatedAt = now };
            _agentDbContext.ChatMessages.Add(userMessage);
        }

        // Persist renderable tool calls (e.g. get_weather) as a Tool-role message so the card can be
        // rehydrated on reload. It is excluded from the AI history (see PrepareConversationHistory) and
        // dated between the user question and the assistant reply so it renders ahead of the text.
        if (!string.IsNullOrEmpty(toolRenderJson))
        {
            var toolMessage = new PChatMessage
            {
                UserId = userId,
                Conversation = conversation,
                Content = toolRenderJson,
                Role = ChatRole.Tool,
                CreatedAt = now.AddMilliseconds(1),
                UpdatedAt = now.AddMilliseconds(1)
            };
            _agentDbContext.ChatMessages.Add(toolMessage);
        }

        _agentDbContext.ChatMessages.Add(assistantMessage);

        await _agentDbContext.SaveChangesAsync();

        return assistantMessage;
    }

    // Records a forwarded tool call (callId → name + raw-JSON arguments) so it can be paired with its result.
    private static void CollectToolCall(string content, Dictionary<string, (string Name, string Arguments)> pending)
    {
        try
        {
            var el = JsonDocument.Parse(content).RootElement;
            var callId = el.TryGetProperty("callId", out var c) ? c.GetString() : null;
            var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(name)) return;
            var arguments = el.TryGetProperty("arguments", out var a) ? a.GetRawText() : "{}";
            pending[callId] = (name, arguments);
        }
        catch (JsonException) { /* ignore malformed chunk */ }
    }

    // Builds a persisted envelope { callId, name, arguments, result } from a tool result chunk, if its
    // call was previously recorded. Returns null for results we don't render (e.g. frontend tools).
    private static object? BuildToolRenderEnvelope(string content, Dictionary<string, (string Name, string Arguments)> pending)
    {
        try
        {
            var el = JsonDocument.Parse(content).RootElement;
            var callId = el.TryGetProperty("callId", out var c) ? c.GetString() : null;
            if (string.IsNullOrEmpty(callId) || !pending.TryGetValue(callId, out var call)) return null;
            var result = el.TryGetProperty("result", out var r) ? r.GetString() ?? string.Empty : string.Empty;
            return new { callId, name = call.Name, arguments = call.Arguments, result };
        }
        catch (JsonException) { return null; }
    }

    private async IAsyncEnumerable<PromptResponse> InvokePromptStreamingInternalAsync(
        PromptRequestForm promptRequest,
        List<PChatMessage> history,
        List<string> tags,
        List<string> ocrDocuments,
        TokenUsageInfo tokenUsageInfo,
        Guid? userId,
        bool isAdmin,
        ChatClientCapabilities capabilities)
    {
        if (promptRequest.AgentId == new Guid("760887e0-babd-41ae-aec1-b6ac3803d348"))
        {
            await foreach (var response in TestOrchestratorInvokePromptStreamingInternalAsync(promptRequest, history, tags, userId, isAdmin))
            {
                yield return new PromptResponse(response);
            }
        }
        else
        {
            // Speculative prefetch: start the knowledge search now, using the raw user prompt,
            // so it runs concurrently with agent construction (incl. the MCP ListToolsAsync hop)
            // and the first "decide to search" LLM call. KnowledgePlugin reuses this warm result
            // when the model's memory-tool query matches the prompt, and falls back to a fresh
            // query otherwise. If the model never searches, the task's result is simply discarded.
            var prefetch = _knowledgeService.SearchAsync(promptRequest.Prompt, promptRequest.AgentId, tags);

            // Build the agent up front. A misconfigured agent (e.g. no model provider
            // selected) throws here; catch it so we can stream a friendly message
            // instead of letting the exception corrupt the JSON response stream — a
            // mid-stream throw surfaces on the client as a confusing deserialization
            // error rather than the real cause.
            AIAgent? agent = null;
            string? configError = null;
            try
            {
                agent = await _agentFactory.CreateAgent(promptRequest.AgentId, userId, isAdmin);
            }
            catch (NotSupportedException)
            {
                // C# disallows yield inside a catch, so record the message and emit it below.
                configError = "This agent has no model provider configured. Open the agent settings and select a provider, model, and API key before chatting.";
            }

            if (configError is not null)
            {
                yield return new PromptResponse(configError);
                yield break;
            }

            var chatHistory = new List<ChatMessage>();

            foreach (var msg in history.OrderBy(m => m.CreatedAt))
            {
                chatHistory.Add(new ChatMessage(msg.Role, msg.Content));
            }

            var prompt = BuildPromptAsync(promptRequest, ocrDocuments);

            var userMessage = BuildUserMessage(promptRequest, prompt);

            chatHistory.Add(userMessage);

            AITool memorySearch = new KnowledgePlugin(_knowledgeService, tags, promptRequest.AgentId, promptRequest.Prompt, prefetch).AsAITool();

            // The outer agent's own knowledge tool is attached per-request here; inner-agent
            // ("agent-as-a-tool") tools are now baked into the agent by AgentFactory
            // (GetInnerAgentToolsAsync), gated to the caller via the userId/isAdmin passed to
            // CreateAgent above. Each inner agent is wrapped by AgentToolPlugin, which re-checks
            // access at call time and scopes the child to its own LightRAG workspace.
            var tools = new List<AITool> { memorySearch };

            // Agent Skills. Gated by the AgentSkills bindings rather than the AgentTools table that
            // GetAgentToolsByAgentId filters on, so they are attached here alongside the knowledge
            // tool rather than baked in by AgentFactory. An agent with no bound skills gets neither
            // the tools nor the catalog message, leaving its runs byte-identical to before.
            //
            // Withheld outright from a text-only client, rather than trimmed down to the tools it
            // could technically run. What the skills we ship produce is an A2UI surface, and only
            // the AG-UI client mounts a renderer for one: render_skill_surface there would draw
            // something nobody can see, and its result — the surface's whole component tree,
            // routinely ~100 KB of JSON — would arrive in the Blazor chat as text. Leaving
            // load_skill on by itself is the worse half-measure, because it hands the model a
            // step-by-step flow whose every step names a tool it was not given. So the tier stack
            // goes as a unit, and a text-only run stays byte-identical to a run on an agent with no
            // skills bound.
            IReadOnlyList<SkillRegistry.ActiveSkill> activeSkills = [];

            if (capabilities == ChatClientCapabilities.GenerativeUi)
            {
                // Guarded for the same reason conversation naming is: this runs before the first
                // yield of an async iterator, so an exception escapes ChatStreamingAsync entirely
                // and surfaces as RUN_ERROR with no answer — for every agent, including the ones
                // with no skills bound. Skills are decoration on a run; they degrade, they do not
                // abort it.
                try
                {
                    activeSkills = await _skillRegistry.GetActiveSkillsAsync(promptRequest.AgentId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load skills for agent {AgentId}; running without them", promptRequest.AgentId);
                }
            }

            if (activeSkills.Count > 0)
            {
                tools.Add(SkillTools.CreateLoadSkill(_skillRegistry, _skillActivityLog, promptRequest.AgentId, _logger));
                tools.Add(SkillTools.CreateRenderSurface(
                    _skillRegistry, _renderableToolCapture, _skillActivityLog, promptRequest.AgentId, _logger));
            }

            var chatOptions = new ChatOptions
            {
                Tools = tools
            };

            // AG-UI frontend tools: declared to the LLM but executed in the browser.
            // Declaration-only tools are not invocable, so the model's call surfaces below
            // as FunctionCallContent instead of being executed server-side.
            var frontendToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Read only for the client that owns it. A text-only client executes no frontend tool,
            // so a declaration from one could only ever produce a call nothing answers. It is also
            // the field's only protection: PromptRequestForm is [FromForm]-bound on that endpoint,
            // so FrontendToolsJson is caller-supplied there, and a crafted form post naming
            // render_a2ui would otherwise pull the entire A2UI render guide into a run whose client
            // cannot draw a surface.
            if (capabilities == ChatClientCapabilities.GenerativeUi
                && !string.IsNullOrWhiteSpace(promptRequest.FrontendToolsJson))
            {
                // FrontendToolsJson comes from the request body, so a client can declare any tool
                // name it likes — including one already registered server-side. Providers reject
                // duplicate function names outright, which would turn a crafted request into a
                // failed run for that user. Server-side tools win; a collision is dropped, not
                // added, and logged rather than silently ignored.
                var serverToolNames = chatOptions.Tools
                    .OfType<AIFunction>()
                    .Select(t => t.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var tool in FrontendToolDeclaration.ParseFromJson(promptRequest.FrontendToolsJson))
                {
                    if (!serverToolNames.Add(tool.Name))
                    {
                        _logger.LogWarning(
                            "Frontend tool '{ToolName}' collides with a server-side tool and was ignored", tool.Name);
                        continue;
                    }

                    chatOptions.Tools.Add(tool);
                    frontendToolNames.Add(tool.Name);
                }
            }

            // A2UI: when the client's middleware has injected the render_a2ui tool, give the
            // model the A2UI v0.9 component catalog so it can generate valid UI surfaces.
            // (The middleware ships this guidance via the AG-UI context channel, which this
            // backend does not forward — so we inject it as a leading system message here.)
            // Tier 1 of the skill spec's progressive disclosure: names and descriptions only. The
            // bodies stay in the database until the model calls load_skill, so binding ten skills
            // to an agent costs ten lines of context per run rather than ten documents.
            //
            // Inserted BEFORE the render guide so that, after both Insert(0, …) calls, the final
            // order is [render guide, skill catalog, …history…]. Each insert pushes the previous
            // one further from the user's turn, so writing them in the intuitive order would bury
            // the catalog behind 131 lines of A2UI guidance.
            var skillCatalog = SkillPrompt.BuildCatalog(activeSkills);
            if (skillCatalog is not null)
            {
                chatHistory.Insert(0, new ChatMessage(ChatRole.System, skillCatalog));
            }

            if (frontendToolNames.Contains(A2uiPrompt.RenderToolName))
            {
                chatHistory.Insert(0, new ChatMessage(ChatRole.System, A2uiPrompt.RenderGuide));
            }

            await foreach (var update in agent.RunStreamingAsync(chatHistory, options: new ChatClientAgentRunOptions(chatOptions)))
            {
                // Emit any renderable server-side tool calls (e.g. get_weather) captured so far —
                // including ones executed inside an inner agent — so the browser can render them.
                foreach (var chunk in DrainRenderableToolCalls(capabilities))
                {
                    yield return chunk;
                }

                // Drained before this update's own content so a notice ("Using the X skill.")
                // lands before the reasoning for the step it describes, rather than after it.
                foreach (var chunk in DrainSkillNotices(capabilities))
                {
                    yield return chunk;
                }

                foreach (var item in update.Contents)
                {
                    if (item is TextReasoningContent reasoningContent)
                    {
                        yield return new PromptResponse(reasoningContent.Text, PromptContentType.Thinking);
                    }
                    else if (item is TextContent textContent)
                    {
                        yield return new PromptResponse(textContent.Text);
                    }
                    else if (item is FunctionCallContent functionCall && frontendToolNames.Contains(functionCall.Name))
                    {
                        var payload = JsonSerializer.Serialize(new
                        {
                            callId = functionCall.CallId,
                            name = functionCall.Name,
                            arguments = functionCall.Arguments
                        });
                        yield return new PromptResponse(payload, PromptContentType.ToolCall);
                    }
                }
                ExtractTokenUsage(update.RawRepresentation, tokenUsageInfo);
            }

            // Flush any tool captures that arrived during/after the final streamed update.
            foreach (var chunk in DrainRenderableToolCalls(capabilities))
            {
                yield return chunk;
            }

            // Flush any skill narration that arrived during/after the final streamed update.
            foreach (var chunk in DrainSkillNotices(capabilities))
            {
                yield return chunk;
            }
        }
    }

    private static void ExtractTokenUsage(object? rawRepresentation, TokenUsageInfo tokenUsage)
    {
        if (rawRepresentation is not ChatResponseUpdate update) return;
        var usageContent = update.Contents?.OfType<UsageContent>().FirstOrDefault();
        ExtractTokenUsage(usageContent?.Details, tokenUsage);
    }
    private static void ExtractTokenUsage(UsageDetails? usageDetails, TokenUsageInfo tokenUsage)
    {
        if (usageDetails == null) return;
        tokenUsage.InputTokens = usageDetails.InputTokenCount;
        tokenUsage.OutputTokens = usageDetails.OutputTokenCount;
        tokenUsage.ReasoningTokens = usageDetails.ReasoningTokenCount;
        tokenUsage.TotalTokens = usageDetails.TotalTokenCount;
    }

    private async IAsyncEnumerable<string> TestOrchestratorInvokePromptStreamingInternalAsync(
        PromptRequestForm promptRequest,
        List<PChatMessage> history,
        List<string> tags,
        Guid? userId,
        bool isAdmin = false)
    {
        var triageAgent = await _agentFactory.CreateAgent(promptRequest.AgentId, userId, isAdmin);
        var csharpAgent = await _agentFactory.CreateAgent(new Guid("684604F0-3362-4499-A9B9-24AF973DCEBA"), userId, isAdmin); // Gemini Agent ID
        var javaAgent = await _agentFactory.CreateAgent(new Guid("25ACDA2A-413F-49B6-BBE3-CE1435885F3F"), userId, isAdmin); // Azure OpenAI Agent ID
        
        // Suppress MAAIW001 as CreateHandoffBuilderWith is marked for evaluation purposes
        #pragma warning disable MAAIW001
        var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
            .WithHandoffs(triageAgent, [csharpAgent, javaAgent])
            .Build();
        #pragma warning restore MAAIW001

        var chatHistory = new List<ChatMessage>();
        foreach (var msg in history.OrderBy(m => m.CreatedAt))
        {
            chatHistory.Add(new ChatMessage(msg.Role, msg.Content));
        }

        var prompt = BuildPromptAsync(promptRequest, []);

        var userMessage = BuildUserMessage(promptRequest, prompt);

        chatHistory.Add(userMessage);
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, chatHistory);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            if (evt is WorkflowOutputEvent e)
            {
                yield return e.Data?.ToString() ?? string.Empty;
            }
        }
        // TODO: Extract token usage from workflow run if possible
    }

    private static ChatMessage BuildUserMessage(PromptRequestForm promptRequest, string prompt)
    {
        var userMessage = new ChatMessage(ChatRole.User, prompt);

        return userMessage;
    }

    private static string BuildPromptAsync(PromptRequest<UploadItemForm> promptRequest, List<string> ocrDocuments)
    {
        if (ocrDocuments.Count != 0)
        {
            return BuildOcrPromptAsync(promptRequest.Prompt, ocrDocuments);
        }

        return BuildTextOnlyPrompt(promptRequest.Prompt);

    }

    private async Task<string> GenerateConversationName(string question, TokenUsageInfo tokenUsageInfo)
    {
        try
        {
            var agent = await _agentFactory.CreateBasicAgent("Generate a short, descriptive conversation name (≤ 5 words).");
            var results = await agent.RunAsync(question);
            ExtractTokenUsage(results.Usage, tokenUsageInfo);
            return results.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate conversation name, using fallback.");
            return "New Conversation";
        }
    }

    private async Task<string> SummarizeMessagesAsync(List<PChatMessage> messages, TokenUsageInfo tokenUsageInfo)
    {
        if (messages.Count == 0) return string.Empty;

        try
        {
            var chatHistory = new List<ChatMessage>();
            foreach (var msg in messages)
            {
                chatHistory.Add(new ChatMessage(msg.Role, msg.Content));
            }

            var agent = await _agentFactory.CreateBasicAgent("Summarize the following chat into a concise paragraph that captures key points.");
            var runResults = await agent.RunAsync(chatHistory);

            ExtractTokenUsage(runResults.Usage, tokenUsageInfo);
            return runResults.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to summarize messages, returning empty.");
            return string.Empty;
        }
    }

    private static string BuildTextOnlyPrompt(string userPrompt) =>
        $@"
            Question: {userPrompt}. Context: Use search knowledge base tool if available.
            Given the context and provided history information, tools definitions and prior knowledge, reply to the user question. Include citations to the context where appropriate.
            If the answer is not in the context, try to use the search online tool if available or inform the user that you can't answer the question.
        ";


    private static string BuildOcrPromptAsync(string userPrompt, List<string> ocrDocuments)
    {
        var prompt = $@"
            You are a helpful document assistant.
            I will provide one or more documents with text, tables, and selection marks.
            Answer the user's question naturally, as a human would.
            Do not invent information or include irrelevant details.

            Documents:
            {string.Join(Environment.NewLine + Environment.NewLine, ocrDocuments)}

            User query: {userPrompt}
            ";

        return prompt;
    }

    private async Task TrackTokenUsageAsync(
        Guid? userId,
        string? sessionId,
        Guid agentId,
        ConversationListItem conversation,
        Guid? messageId,
        string operationType,
        TokenUsageInfo tokenUsageInfo,
        TimeSpan responseTime)
    {
        var agentConfig = await _agentDbContext.Agents
            .Include(a => a.Provider)
            .FirstOrDefaultAsync(a => a.Id == agentId);
        if (agentConfig == null) return;

        // The ResponseTime column is a SQL `time` (00:00:00–23:59:59). Durations computed from
        // DateTime.UtcNow can go negative when the clock is stepped backwards mid-request
        // (common with WSL2 time sync) — clamp so telemetry never crashes the chat stream.
        if (responseTime < TimeSpan.Zero) responseTime = TimeSpan.Zero;

        var sessionIdGuid = !userId.HasValue && Guid.TryParse(sessionId, out var sid) ? sid : (Guid?)null;

        var tokenUsage = new TokenUsage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionIdGuid,
            ConversationId = conversation.Id,
            MessageId = messageId,
            AgentId = agentId,
            ModelName = agentConfig.ModelOverride ?? string.Empty,
            ProviderName = agentConfig.Provider?.Name ?? string.Empty,
            InputTokens = tokenUsageInfo.InputTokens,
            OutputTokens = tokenUsageInfo.OutputTokens,
            ReasoningTokens = tokenUsageInfo.ReasoningTokens,
            TotalTokens = tokenUsageInfo.TotalTokens,
            InputTokenCost = null,
            OutputTokenCost = null,
            ReasoningTokenCost = null,
            TotalCost = null,
            OperationType = operationType,
            ResponseTime = responseTime,
            CreatedAt = DateTime.UtcNow
        };

        _agentDbContext.TokenUsages.Add(tokenUsage);
        await _agentDbContext.SaveChangesAsync();
    }
}
