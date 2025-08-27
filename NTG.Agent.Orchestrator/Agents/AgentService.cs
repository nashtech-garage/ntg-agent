using Microsoft.EntityFrameworkCore;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Knowledge;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Constants;
using NTG.Agent.Shared.Dtos.Enums;
using System.Text;

namespace NTG.Agent.Orchestrator.Agents;

public class AgentService
{
    private readonly Kernel _kernel;
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IOcrEngine? _ocrEngine;
    private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

    public AgentService(
        Kernel kernel,
        AgentDbContext agentDbContext,
        IKnowledgeService knowledgeService)
    {
        _kernel = kernel;
        _agentDbContext = agentDbContext;
        _knowledgeService = knowledgeService;
        _ocrEngine = kernel.Services.GetService<IOcrEngine>();
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequest promptRequest)
    {
        var conversation = await ValidateConversation(userId, promptRequest);
        var history = await PrepareConversationHistory(userId, conversation);
        var tags = await GetUserTags(userId);

        var agentMessageSb = new StringBuilder();
        await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest, history, tags))
        {
            agentMessageSb.Append(item);
            yield return item;
        }

        await SaveMessages(userId, conversation, promptRequest.Prompt, agentMessageSb.ToString());
    }

    #region Conversation Helpers

    private async Task<Conversation> ValidateConversation(Guid? userId, PromptRequest promptRequest)
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

    private async Task<List<ChatMessage>> PrepareConversationHistory(Guid? userId, Conversation conversation)
    {
        var historyMessages = await _agentDbContext.ChatMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.UpdatedAt)
            .ToListAsync();

        if (historyMessages.Count <= MAX_LATEST_MESSAGE_TO_KEEP_FULL) return historyMessages;

        var toSummarize = historyMessages.Take(historyMessages.Count - MAX_LATEST_MESSAGE_TO_KEEP_FULL).ToList();
        var summary = await SummarizeMessagesAsync(toSummarize);

        var summaryMsg = historyMessages.FirstOrDefault(m => m.IsSummary) ?? new ChatMessage
        {
            UserId = userId,
            Conversation = conversation,
            Role = ChatRole.System,
            IsSummary = true
        };

        summaryMsg.Content = $"Summary: {summary}";
        summaryMsg.UpdatedAt = DateTime.UtcNow;

        _agentDbContext.Update(summaryMsg);

        return new List<ChatMessage> { summaryMsg }
            .Concat(historyMessages.TakeLast(MAX_LATEST_MESSAGE_TO_KEEP_FULL))
            .ToList();
    }

    private async Task SaveMessages(Guid? userId, Conversation conversation, string userPrompt, string assistantReply)
    {
        if (conversation.Name == "New Conversation")
        {
            conversation.Name = await GenerateConversationName(userPrompt);
            _agentDbContext.Conversations.Update(conversation);
        }

        _agentDbContext.ChatMessages.AddRange(
            new ChatMessage { UserId = userId, Conversation = conversation, Content = userPrompt, Role = ChatRole.User },
            new ChatMessage { UserId = userId, Conversation = conversation, Content = assistantReply, Role = ChatRole.Assistant }
        );

        await _agentDbContext.SaveChangesAsync();
    }

    #endregion

    #region Prompt Building + Streaming

    private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(
        PromptRequest promptRequest,
        List<ChatMessage> history,
        List<string> tags)
    {
        var chatHistory = new ChatHistory();
        foreach (var msg in history.OrderBy(m => m.CreatedAt))
        {
            var role = msg.Role switch
            {
                ChatRole.User => AuthorRole.User,
                ChatRole.Assistant => AuthorRole.Assistant,
                _ => AuthorRole.System
            };
            chatHistory.AddMessage(role, msg.Content);
        }

        // Build prompt based on input
        var prompt = await BuildPromptAsync(promptRequest);

        var userMessage = new ChatMessageContent { Role = AuthorRole.User };
        userMessage.Items.Add(new TextContent(prompt));

        if (string.IsNullOrEmpty(promptRequest.ImageBase64) == false && _ocrEngine == null)
        {
            // Only attach raw image if OCR not available
            userMessage.Items.Add(new ImageContent(
                Convert.FromBase64String(promptRequest.ImageBase64),
                promptRequest.ImageContentType ?? "image/png"));
        }

        chatHistory.Add(userMessage);

        var kernel = _kernel.Clone();
        kernel.ImportPluginFromObject(new KnowledgePlugin(_knowledgeService, tags), "memory");

        var agent = new ChatCompletionAgent
        {
            Name = "NTG-Assistant",
            Instructions = "You are an NTG AI Assistant. Be helpful and precise.",
            Kernel = kernel,
            Arguments = new KernelArguments(new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
        };

        await foreach (var item in agent.InvokeStreamingAsync(chatHistory))
            yield return item.Message.ToString();
    }

    private async Task<string> BuildPromptAsync(PromptRequest promptRequest)
    {
        if (string.IsNullOrEmpty(promptRequest.ImageBase64))
        {
            // Text only
            return $@"
Search the knowledge base: {promptRequest.Prompt}
Knowledge base will answer: {{memory.search}}
If the answer is empty, continue answering with your knowledge and plugins.";
        }

        if (_ocrEngine != null)
        {
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(promptRequest.ImageBase64));
                var text = await _ocrEngine.ExtractTextFromImageAsync(ms);

                return $@"
                You are given:
                - An extracted text: {text}
                - User query: {promptRequest.Prompt}

                Your task:
                1. Start by clearly presenting the extracted text in a natural, friendly way.
                2. Search the knowledge base: {{memory.search}}.
                   - If you find results that are related to the extracted text and be useful, naturally weave them into your answer with a short reference or link.
                   - If the results are irrelevant or add no value, do not mention them.
                3. If the knowledge base has nothing useful, simply answer using the extracted text and the user query.
                4. Keep your tone clear, conversational, and helpful. Do not list unrelated documents or say they are irrelevant.";
            }
            catch (Exception ex)
            {
                return $"[OCR failed: {ex.Message}]. Proceed analyzing image with query: {promptRequest.Prompt}";
            }
        }

        // Fallback: multimodal without OCR
        return $@"
You are given a user query and an image. 
1. Analyze the image (objects, text, context). 
2. Combine with query: {promptRequest.Prompt}
3. Search knowledge base: {{memory.search}}
4. If useful info is found, include it with citations. 
5. Otherwise, answer from reasoning on query + image.";
    }

    #endregion

    #region Helpers

    private async Task<string> GenerateConversationName(string question)
    {
        var agent = new ChatCompletionAgent
        {
            Name = "ConversationNameGenerator",
            Instructions = "Generate a short, descriptive conversation name (≤ 5 words).",
            Kernel = _kernel
        };

        var sb = new StringBuilder();
        await foreach (var res in agent.InvokeAsync(question)) sb.Append(res.Message);
        return sb.ToString();
    }

    private async Task<string> SummarizeMessagesAsync(List<ChatMessage> messages)
    {
        if (messages.Count == 0) return string.Empty;

        var chatHistory = new ChatHistory();
        foreach (var msg in messages)
        {
            var role = msg.Role switch
            {
                ChatRole.User => AuthorRole.User,
                ChatRole.Assistant => AuthorRole.Assistant,
                _ => AuthorRole.System
            };
            chatHistory.AddMessage(role, msg.Content);
        }

        var summarizer = new ChatCompletionAgent
        {
            Name = "ConversationSummarizer",
            Instructions = "Summarize the chat into a concise paragraph.",
            Kernel = _kernel
        };

        var sb = new StringBuilder();
        await foreach (var res in summarizer.InvokeAsync(chatHistory)) sb.Append(res.Message);
        return sb.ToString();
    }

    #endregion
}
