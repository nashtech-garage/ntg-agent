using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Common.Dtos.Constants;
using NTG.Agent.Common.Dtos.TokenUsage;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Models.TokenUsage;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Orchestrator.Services.Memory;
using System.Text;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

namespace NTG.Agent.Orchestrator.Services.Agents;

public class AgentService
{
    private readonly IAgentFactory _agentFactory;
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IUserMemoryService _memoryService;
    private readonly ILogger<AgentService> _logger;
    private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

    public AgentService(
        IAgentFactory agentFactory,
        AgentDbContext agentDbContext,
        IKnowledgeService knowledgeService,
        IUserMemoryService memoryService,
        ILogger<AgentService> logger)
    {
        _agentFactory = agentFactory;
        _agentDbContext = agentDbContext;
        _knowledgeService = knowledgeService;
        _memoryService = memoryService;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequestForm promptRequest)
    {
        var startTime = DateTime.UtcNow;
        var conversation = await ValidateConversation(userId, promptRequest);
        var history = await PrepareConversationHistory(userId, promptRequest.SessionId, promptRequest.AgentId, conversation);
        var tags = await GetUserTags(userId);
        var ocrDocuments = new List<string>();
        if (promptRequest.Documents is not null && promptRequest.Documents.Any())
        {
            //ocrDocuments = await _documentAnalysisService.ExtractDocumentData(promptRequest.Documents);
        }
        var agentMessageSb = new StringBuilder();
        var tokenUsageInfo = new TokenUsageInfo();

        await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest, history, tags, ocrDocuments, tokenUsageInfo, userId))
        {
            agentMessageSb.Append(item);
            yield return item;
        }

        var responseTime = DateTime.UtcNow - startTime;
        var savedMessage = await SaveMessages(userId, promptRequest, conversation, agentMessageSb.ToString(), ocrDocuments);

        await TrackTokenUsageAsync(userId, promptRequest.SessionId, promptRequest.AgentId, new ConversationListItem(conversation.Id, conversation.Name), savedMessage.Id, OperationTypes.Chat, tokenUsageInfo, responseTime);
        
        // Store or update memories if needed
        if (userId is Guid userGuid)
        {
            var memoryResults = await _memoryService.ExtractMemoryAsync(promptRequest.Prompt, userGuid);
            foreach (var memoryResult in memoryResults)
            {
                if (memoryResult.ShouldWriteMemory && 
                    memoryResult.Confidence.HasValue && 
                    memoryResult.Confidence.Value > 0.3f && 
                    !string.IsNullOrWhiteSpace(memoryResult.MemoryToWrite))
                {
                    // Check if this is an update/correction
                    if (!string.IsNullOrWhiteSpace(memoryResult.SearchQuery))
                    {
                        var existingMemories = await _memoryService.RetrieveMemoriesByFieldAsync(
                            userGuid,
                            fieldTag: memoryResult.SearchQuery,
                            category: memoryResult.Category);

                        // Delete conflicting memories
                        foreach (var oldMemory in existingMemories)
                        {
                            await _memoryService.DeleteMemoryAsync(oldMemory.Id);
                        }
                    }

                    // Store the new/updated memory
                    await _memoryService.StoreMemoryAsync(
                        userGuid,
                        memoryResult.MemoryToWrite,
                        memoryResult.Category ?? "general",
                        memoryResult.Tags);
                }
            }
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
            .Where(m => m.ConversationId == conversation.Id)
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

    private async Task<PChatMessage> SaveMessages(Guid? userId, PromptRequestForm promptRequest, Conversation conversation, string assistantReply, List<string> ocrDocuments)
    {
        if (conversation.Name == "New Conversation")
        {
            var tokenUsageInfo = new TokenUsageInfo();
            var startTime = DateTime.UtcNow;
            conversation.Name = await GenerateConversationName(promptRequest.Prompt, tokenUsageInfo);
            var responseTime = DateTime.UtcNow - startTime;

            _agentDbContext.Conversations.Update(conversation);
            await _agentDbContext.SaveChangesAsync();

            await TrackTokenUsageAsync(userId, promptRequest.SessionId, promptRequest.AgentId, new ConversationListItem(conversation.Id, conversation.Name), null, OperationTypes.GenerateName, tokenUsageInfo, responseTime);
        }

        var userMessage = new PChatMessage { UserId = userId, Conversation = conversation, Content = promptRequest.Prompt, Role = ChatRole.User };
        var assistantMessage = new PChatMessage { UserId = userId, Conversation = conversation, Content = assistantReply, Role = ChatRole.Assistant };

        _agentDbContext.ChatMessages.AddRange(userMessage, assistantMessage);

        await _agentDbContext.SaveChangesAsync();

        return assistantMessage;
    }

    private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(
        PromptRequestForm promptRequest,
        List<PChatMessage> history,
        List<string> tags,
        List<string> ocrDocuments,
        TokenUsageInfo tokenUsageInfo,
        Guid? userId)
    {
        if (promptRequest.AgentId == new Guid("760887e0-babd-41ae-aec1-b6ac3803d348"))
        {
            await foreach (var response in TestOrchestratorInvokePromptStreamingInternalAsync(promptRequest, history, tags, userId))
            {
                yield return response;
            }
        }
        else
        {
            var agent = await _agentFactory.CreateAgent(promptRequest.AgentId);

            var chatHistory = new List<ChatMessage>();

            // Inject long-term memories at the beginning for authenticated users
            if (userId is Guid userIdGuid)
            {
                // Use the user's current prompt for semantic memory retrieval
                var memories = await _memoryService.RetrieveMemoriesAsync(
                    userIdGuid, 
                    query: promptRequest.Prompt,
                    topN: 10);
                
                if (memories.Count > 0)
                {
                    var memoryContext = _memoryService.FormatMemoriesForPrompt(memories);
                    chatHistory.Add(new ChatMessage(ChatRole.System, memoryContext));
                }
            }

            foreach (var msg in history.OrderBy(m => m.CreatedAt))
            {
                chatHistory.Add(new ChatMessage(msg.Role, msg.Content));
            }

            var prompt = BuildPromptAsync(promptRequest, ocrDocuments);

            var userMessage = BuildUserMessage(promptRequest, prompt);

            chatHistory.Add(userMessage);

            AITool memorySearch = new KnowledgePlugin(_knowledgeService, tags, promptRequest.AgentId).AsAITool();

            var chatOptions = new ChatOptions
            {
                Tools = [memorySearch]
            };

            await foreach (var item in agent.RunStreamingAsync(chatHistory, options: new ChatClientAgentRunOptions(chatOptions)))
            {
                if (!string.IsNullOrEmpty(item.Text))
                {
                    yield return item.Text;
                }
                ExtractTokenUsage(item.RawRepresentation, tokenUsageInfo);
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
        tokenUsage.TotalTokens = usageDetails.TotalTokenCount;
    }

    private async IAsyncEnumerable<string> TestOrchestratorInvokePromptStreamingInternalAsync(
        PromptRequestForm promptRequest,
        List<PChatMessage> history,
        List<string> tags,
        Guid? userId)
    {
        var triageAgent = await _agentFactory.CreateAgent(promptRequest.AgentId);
        var csharpAgent = await _agentFactory.CreateAgent(new Guid("684604F0-3362-4499-A9B9-24AF973DCEBA")); // Gemini Agent ID
        var javaAgent = await _agentFactory.CreateAgent(new Guid("25ACDA2A-413F-49B6-BBE3-CE1435885F3F")); // Azure OpenAI Agent ID
        var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
            .WithHandoffs(triageAgent, [csharpAgent, javaAgent])
            .Build();

        var chatHistory = new List<ChatMessage>();
        foreach (var msg in history.OrderBy(m => m.CreatedAt))
        {
            chatHistory.Add(new ChatMessage(msg.Role, msg.Content));
        }

        var prompt = BuildPromptAsync(promptRequest, []);

        var userMessage = BuildUserMessage(promptRequest, prompt);

        chatHistory.Add(userMessage);
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, chatHistory);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            if (evt is AgentRunUpdateEvent e)
            {
                yield return e.Data?.ToString() ?? string.Empty;
            }
        }
        // Inject long-term memories for authenticated users
        if (userId is Guid userIdGuid)
        {
            // Use the user's current prompt for semantic memory retrieval
            var memories = await _memoryService.RetrieveMemoriesAsync(
                userIdGuid, 
                query: promptRequest.Prompt,  // Semantic search based on current message
                topN: 10);
            
            if (memories.Count > 0)
            {
                var memoryContext = _memoryService.FormatMemoriesForPrompt(memories);
                chatHistory.Add(new ChatMessage(ChatRole.System, memoryContext));
            }
        }
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
        var agent = await _agentFactory.CreateBasicAgent("Generate a short, descriptive conversation name (≤ 5 words).");
        var results = await agent.RunAsync(question);
        ExtractTokenUsage(results.Usage, tokenUsageInfo);
        return results.Text;
    }

    private async Task<string> SummarizeMessagesAsync(List<PChatMessage> messages, TokenUsageInfo tokenUsageInfo)
    {
        if (messages.Count == 0) return string.Empty;

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
        var agentConfig = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == agentId);
        if (agentConfig == null) return;

        var sessionIdGuid = !userId.HasValue && Guid.TryParse(sessionId, out var sid) ? sid : (Guid?)null;

        var tokenUsage = new TokenUsage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionIdGuid,
            ConversationId = conversation.Id,
            MessageId = messageId,
            AgentId = agentId,
            ModelName = agentConfig.ProviderModelName,
            ProviderName = agentConfig.ProviderName,
            InputTokens = tokenUsageInfo.InputTokens,
            OutputTokens = tokenUsageInfo.OutputTokens,
            TotalTokens = tokenUsageInfo.TotalTokens,
            InputTokenCost = null,
            OutputTokenCost = null,
            TotalCost = null,
            OperationType = operationType,
            ResponseTime = responseTime,
            CreatedAt = DateTime.UtcNow
        };

        _agentDbContext.TokenUsages.Add(tokenUsage);
        await _agentDbContext.SaveChangesAsync();
    }
}
