using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Orchestrator.Services.WebSearch;
using NTG.Agent.Shared.Dtos.Constants;
using NTG.Agent.Shared.Dtos.Enums;
using System.Text;

namespace NTG.Agent.Orchestrator.Agents;

public class AgentService
{
    private readonly Kernel _kernel;
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ITextSearchService _textSearchService;
    private readonly IDocumentAnalysisService _documentAnalysisService;
    private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

    public record PromptStreamingContext(
    PromptRequestForm PromptRequest,
    List<ChatMessage> History,
    List<string> Tags,
    List<string> OcrDocuments);

    public AgentService(
        Kernel kernel,
        AgentDbContext agentDbContext,
        IKnowledgeService knowledgeService,
        IDocumentAnalysisService documentAnalysisService,
        ITextSearchService textSearchService
         )
    {
        _kernel = kernel;
        _agentDbContext = agentDbContext;
        _knowledgeService = knowledgeService;
        _documentAnalysisService = documentAnalysisService;
        _textSearchService = textSearchService;
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequestForm promptRequest)
    {
        var conversation = await ValidateConversation(userId, promptRequest);
        var history = await PrepareConversationHistory(userId, conversation);
        var tags = await GetUserTags(userId);
        var ocrDocuments = new List<string>();
        if (promptRequest.Documents is not null && promptRequest.Documents.Any())
        {
            ocrDocuments = await _documentAnalysisService.ExtractDocumentData(promptRequest.Documents);
        }
        var agentMessageSb = new StringBuilder();

        await foreach (var item in InvokePromptStreamingInternalAsync(
            new PromptStreamingContext(
                promptRequest,
                history,
                tags,
                ocrDocuments
            )))
        {
            agentMessageSb.Append(item);
            yield return item;
        }

        await SaveMessages(userId, conversation, promptRequest.Prompt, agentMessageSb.ToString(), ocrDocuments);
    }

    #region Conversation Helpers

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

        summaryMsg.Content = $"Summary of earlier conversation: {summary}";
        summaryMsg.UpdatedAt = DateTime.UtcNow;

        _agentDbContext.Update(summaryMsg);

        return new List<ChatMessage> { summaryMsg }
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
            new ChatMessage { UserId = userId, Conversation = conversation, Content = userPrompt, Role = ChatRole.User },
            new ChatMessage { UserId = userId, Conversation = conversation, Content = assistantReply, Role = ChatRole.Assistant }
        );

        var ocrMessages = ocrDocuments.Select(documentData => new ChatMessage { UserId = userId, Conversation = conversation, Content = documentData, Role = ChatRole.System });
        _agentDbContext.ChatMessages.AddRange(ocrMessages);

        await _agentDbContext.SaveChangesAsync();
    }

    #endregion

    #region Prompt Building + Streaming

    private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(
        PromptStreamingContext context)
    {
        var history = context.History;
        var promptRequest = context.PromptRequest;
        var tags = context.Tags;
        var ocrDocuments = context.OcrDocuments;
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

        var prompt = BuildPromptAsync(promptRequest, ocrDocuments);
        var userMessage = BuildUserMessage(promptRequest, prompt);
        chatHistory.Add(userMessage);

        var kernel = _kernel.Clone();
        kernel.ImportPluginFromObject(new KnowledgePlugin(_knowledgeService, tags, context.PromptRequest.ConversationId), "memory");
        kernel.ImportPluginFromObject(new WebSearchPlugin(_textSearchService, _knowledgeService, _kernel, context.PromptRequest.ConversationId), "onlineweb");

        var agent = new ChatCompletionAgent
        {
            Name = "NTG-Assistant",
            Instructions = @"You are an NGT AI Assistant. Answer questions with all your best.",
            Kernel = kernel,
            Arguments = new KernelArguments(new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
        };

        await foreach (var item in agent.InvokeStreamingAsync(chatHistory))
            yield return item.Message.ToString();
    }


    private ChatMessageContent BuildUserMessage(PromptRequestForm promptRequest, string prompt)
    {
        var userMessage = new ChatMessageContent { Role = AuthorRole.User };
        userMessage.Items.Add(new TextContent(prompt));

        return userMessage;
    }

    private string BuildPromptAsync(PromptRequestForm promptRequest, List<string> ocrDocuments)
    {
        if (ocrDocuments.Any())
        {
            return BuildOcrPromptAsync(promptRequest.Prompt, ocrDocuments);
        }

        return BuildTextOnlyPrompt(promptRequest.Prompt);

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
            Instructions = "Summarize the following chat into a concise paragraph that captures key points.",
            Kernel = _kernel
        };

        var sb = new StringBuilder();
        await foreach (var res in summarizer.InvokeAsync(chatHistory)) sb.Append(res.Message);
        return sb.ToString();
    }

    private string BuildTextOnlyPrompt(string userPrompt) =>
$@"
First, check if the answer can be found in the conversation history.
If it is relevant, answer based on that context.

If the conversation history does not contain the answer,
then search the knowledge base with the query: {userPrompt}
Knowledge base will answer: {{memory.search}}

If the knowledge base does not contain the answer,
then search online web with the query: {userPrompt}
Online web will answer: {{onlineweb.search}}

When using the online web results, always include the provided sources
at the end of your answer in a clear, readable format (e.g., Markdown links).

Answer the question in a clear, natural, human-like way.
If both conversation history and knowledge base are empty,
continue answering with your own knowledge and plugins.";


    private string BuildOcrPromptAsync(string userPrompt,
        List<string> ocrDocuments)
    {
        var prompt = $@"
You are a helpful document assistant.
I will provide one or more documents with text
Answer the user's question naturally, as a human would.
Do not invent information or include irrelevant details.

Documents:
{string.Join(Environment.NewLine + Environment.NewLine, ocrDocuments)}

User query: {userPrompt}
";

        return prompt;
    }

    #endregion
}
