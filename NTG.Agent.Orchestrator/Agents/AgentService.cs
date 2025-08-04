using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Knowledge;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Shared.Dtos.Chats;
using System.Text;

namespace NTG.Agent.Orchestrator.Agents;

public class AgentService
{
    private readonly Kernel _kernel;
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

    public AgentService(Kernel kernel, AgentDbContext agentDbContext, IKnowledgeService knowledgeService)
    {
        _kernel = kernel;
        _agentDbContext = agentDbContext;
        _knowledgeService = knowledgeService;
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequest promptRequest)
    {
        var conversation = await ValidateConversation(userId, promptRequest);

        List<ChatMessage> messagesToUse = await PrepareConversationHistory(userId, conversation);

        var agentMessageSb = new StringBuilder();
        await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest.Prompt, messagesToUse))
        {
            agentMessageSb.Append(item);
            yield return item;
        }

        // Rename conversation if unnamed
        if (conversation.Name == "New Conversation")
        {
            conversation.Name = await GenerateConversationName(promptRequest.Prompt);
            _agentDbContext.Conversations.Update(conversation);
        }

        var userMessage = new ChatMessage
        {
            UserId = userId,
            Conversation = conversation,
            Content = promptRequest.Prompt,
            Role = ChatRole.User
        };

        var assistantMessage = new ChatMessage
        {
            UserId = userId,
            Conversation = conversation,
            Content = agentMessageSb.ToString(),
            Role = ChatRole.Assistant
        };

        _agentDbContext.ChatMessages.AddRange(userMessage, assistantMessage);
        await _agentDbContext.SaveChangesAsync();
    }

    private async Task<Conversation> ValidateConversation(Guid? userId, PromptRequest promptRequest)
    {
        Guid conversationId = promptRequest.ConversationId;
        Conversation? conversation;
        if (userId.HasValue)
        {
            conversation = await _agentDbContext.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);
        }
        else
        {
            if (!Guid.TryParse(promptRequest.SessionId, out Guid sessionId))
            {
                throw new InvalidOperationException("A valid Session ID is required for unauthenticated requests.");
            }
            conversation = await _agentDbContext.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.SessionId == sessionId);
        }

        if (conversation is null)
        {
            throw new InvalidOperationException($"Conversation with ID {conversationId} does not exist.");
        }
        return conversation;
    }

    private async Task<List<ChatMessage>> PrepareConversationHistory(Guid? userId, Conversation conversation)
    {
        var historyMessages = await _agentDbContext.ChatMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.UpdatedAt)
            .ToListAsync();

        if (historyMessages.Count > MAX_LATEST_MESSAGE_TO_KEEP_FULL)
        {
            var messagesToResummarize = historyMessages.Take(historyMessages.Count - MAX_LATEST_MESSAGE_TO_KEEP_FULL).ToList();
            var summarizedContent = await SummarizeMessagesAsync(messagesToResummarize);
            var summaryMessage = historyMessages.Where(m => m.IsSummary).FirstOrDefault();
            if (summaryMessage == null)
            {
                summaryMessage = new ChatMessage
                {
                    UserId = userId,
                    Conversation = conversation,
                    Content = $"Summary of earlier conversation: {summarizedContent}",
                    Role = ChatRole.System,
                    IsSummary = true
                };
                _agentDbContext.ChatMessages.Add(summaryMessage);
            }
            else
            {
                summaryMessage.Content = $"Summary of earlier conversation: {summarizedContent}";
                summaryMessage.UpdatedAt = DateTime.UtcNow;
                _agentDbContext.ChatMessages.Update(summaryMessage);
            }

            var optimizedHistoryMessages = new List<ChatMessage>
            {
                summaryMessage
            };
            optimizedHistoryMessages.AddRange(historyMessages.TakeLast(MAX_LATEST_MESSAGE_TO_KEEP_FULL));
            return optimizedHistoryMessages;
        }
        else
        {
            return historyMessages;
        }
    }

    private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(string message, List<ChatMessage>? previousMessages)
    {
        ChatHistory chatHistory = [];
        if (previousMessages is not null)
        {
            foreach (var msg in previousMessages.OrderBy(m => m.CreatedAt))
            {
                var role = msg.Role switch
                {
                    ChatRole.User => AuthorRole.User,
                    ChatRole.Assistant => AuthorRole.Assistant,
                    _ => AuthorRole.System
                };
                chatHistory.AddMessage(role, msg.Content);
            }
        }

        var arguments = new KernelArguments(new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        });

        Kernel agentKernel = _kernel.Clone();

        var prompt = $@"
               Search to knowledge base: {message}
               Knowledge base will answer: {{memory.search}}
               If the answer is empty, continue answering with your knowledge and tools or plugins. Otherwise reply with the answer and include citations to the relevant information where it is referenced in the response";
        chatHistory.AddMessage(AuthorRole.User, prompt);

        agentKernel.ImportPluginFromObject(new KnowledgePlugin(_knowledgeService), "memory");

        ChatCompletionAgent agent = new()
        {
            Name = "NTG-Assistant",
            Instructions = @"You are an NGT AI Assistant. Answer questions with all your best.",
            Kernel = agentKernel,
            Arguments = arguments,
        };

        var result = agent.InvokeStreamingAsync(chatHistory);
        await foreach (var item in result)
        {
            yield return item.Message.ToString();
        }
    }

    private async Task<string> GenerateConversationName(string question)
    {
        ChatCompletionAgent agent =
            new()
            {
                Name = "ConversationNameGenerator",
                Instructions = @"Generate a concise and descriptive name for the conversation based on the user's question. Maximum 5 words",
                Kernel = _kernel
            };
        var sb = new StringBuilder();
        await foreach (AgentResponseItem<ChatMessageContent> response in agent.InvokeAsync(question))
        {
            sb.Append(response.Message);
        }
        return sb.ToString();
    }

    private async Task<string> SummarizeMessagesAsync(List<ChatMessage> messages)
    {
        if (messages == null || messages.Count == 0) return string.Empty;

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

        ChatCompletionAgent summarizer = new()
        {
            Name = "ConversationSummarizer",
            Instructions = "Summarize the following chat into a concise paragraph that captures key points.",
            Kernel = _kernel
        };

        var sb = new StringBuilder();
        await foreach (var response in summarizer.InvokeAsync(chatHistory))
        {
            sb.Append(response.Message);
        }
        return sb.ToString();
    }

}
