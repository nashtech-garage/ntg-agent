using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Constants;
using System.Text;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

namespace NTG.Agent.Orchestrator.Agents;

public class AgentService
{
    private readonly AgentFactory _agentFactory;
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

    public AgentService(
        AgentFactory agentFactory,
        AgentDbContext agentDbContext,
        IKnowledgeService knowledgeService
         )
    {
        _agentFactory = agentFactory;
        _agentDbContext = agentDbContext;
        _knowledgeService = knowledgeService;
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequestForm promptRequest)
    {
        var conversation = await ValidateConversation(userId, promptRequest);
        var history = await PrepareConversationHistory(userId, conversation);
        var tags = await GetUserTags(userId);
        var ocrDocuments = new List<string>();
        if (promptRequest.Documents is not null && promptRequest.Documents.Any())
        {
            //ocrDocuments = await _documentAnalysisService.ExtractDocumentData(promptRequest.Documents);
        }
        var agentMessageSb = new StringBuilder();
        await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest, history, tags, ocrDocuments))
        {
            agentMessageSb.Append(item);
            yield return item;
        }

        await SaveMessages(userId, conversation, promptRequest.Prompt, agentMessageSb.ToString(), ocrDocuments);
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

    private async Task<List<PChatMessage>> PrepareConversationHistory(Guid? userId, Conversation conversation)
    {
        var historyMessages = await _agentDbContext.ChatMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.UpdatedAt)
            .ToListAsync();

        if (historyMessages.Count <= MAX_LATEST_MESSAGE_TO_KEEP_FULL) return historyMessages;

        var toSummarize = historyMessages.Take(historyMessages.Count - MAX_LATEST_MESSAGE_TO_KEEP_FULL).ToList();
        var summary = await SummarizeMessagesAsync(toSummarize);

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

        return new List<PChatMessage> { summaryMsg }
            .Concat(historyMessages.TakeLast(MAX_LATEST_MESSAGE_TO_KEEP_FULL))
            .ToList();
    }

    private async Task SaveMessages(Guid? userId, Conversation conversation, string userPrompt, string assistantReply, List<string> ocrDocuments)
    {
        if (conversation.Name == "New Conversation")
        {
            conversation.Name = await GenerateConversationName(userPrompt);
            _agentDbContext.Conversations.Update(conversation);
        }

        _agentDbContext.ChatMessages.AddRange(
            new PChatMessage { UserId = userId, Conversation = conversation, Content = userPrompt, Role = ChatRole.User },
            new PChatMessage { UserId = userId, Conversation = conversation, Content = assistantReply, Role = ChatRole.Assistant }
        );

        await _agentDbContext.SaveChangesAsync();
    }

    private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(
        PromptRequestForm promptRequest,
        List<PChatMessage> history,
        List<string> tags,
        List<string> ocrDocuments)
    {
        var agent = _agentFactory.CreateAgent(Guid.Empty);

        var chatHistory = new List<ChatMessage>();
        foreach (var msg in history.OrderBy(m => m.CreatedAt))
        {
            chatHistory.Add(new ChatMessage(msg.Role, msg.Content));
        }

        var prompt = BuildPromptAsync(promptRequest, ocrDocuments);

        var userMessage = BuildUserMessage(promptRequest, prompt);

        chatHistory.Add(userMessage);

        AITool memorySearch = new KnowledgePlugin(_knowledgeService, tags).AsAITool();
        var chatOptions = new ChatOptions
        {
            Tools = [memorySearch]
        };

        await foreach (var item in agent.RunStreamingAsync(chatHistory, options: new ChatClientAgentRunOptions(chatOptions)))
            yield return item.Text;
    }

    private ChatMessage BuildUserMessage(PromptRequestForm promptRequest, string prompt)
    {
        var userMessage = new ChatMessage(ChatRole.User, prompt);

        return userMessage;
    }

    private string BuildPromptAsync(PromptRequest<UploadItemForm> promptRequest, List<string> ocrDocuments)
    {
        if (ocrDocuments.Any())
        {
            return BuildOcrPromptAsync(promptRequest.Prompt, ocrDocuments);
        }

        return BuildTextOnlyPrompt(promptRequest.Prompt);

    }

    private async Task<string> GenerateConversationName(string question)
    {
        var agent = _agentFactory.CreateBasicAgent("Generate a short, descriptive conversation name (≤ 5 words).");
        var results = await agent.RunAsync(question);
        return results.Text;
    }

    private async Task<string> SummarizeMessagesAsync(List<PChatMessage> messages)
    {
        if (messages.Count == 0) return string.Empty;

        var chatHistory = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            chatHistory.Add(new ChatMessage(msg.Role, msg.Content));
        }

        var agent = _agentFactory.CreateBasicAgent("Summarize the following chat into a concise paragraph that captures key points.");
        var runResults = await agent.RunAsync(chatHistory);
        return runResults.Text;
    }

    private string BuildTextOnlyPrompt(string userPrompt) =>
        $@"
            Search for the {userPrompt} in the knowledge base by calling the tool {{memory.search}}.
            If the answer is empty, continue answering with your knowledge and tools or plugins. Otherwise reply with the answer and include citations to the relevant information where it is referenced in the response.
        ";


    private string BuildOcrPromptAsync(string userPrompt, List<string> ocrDocuments)
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
}
