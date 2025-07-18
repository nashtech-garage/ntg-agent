using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.Chats;
using System.Text;

namespace NTG.Agent.Orchestrator.Agents;

public interface IAgentService
{
    IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequest promptRequest);
}

public class AgentService : IAgentService
{
    private readonly Kernel _kernel;
    private readonly AgentDbContext _agentDbContext;
    private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

    public AgentService(Kernel kernel, AgentDbContext agentDbContext)
    {
        _kernel = kernel;
        _agentDbContext = agentDbContext;
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequest promptRequest)
    {
        if (userId.HasValue)
        {
            Guid conversationId = promptRequest.ConversationId;
            var conversation = await _agentDbContext.Conversations.FindAsync(conversationId) ?? throw new InvalidOperationException($"Conversation with ID {conversationId} does not exist.");

            var summaryMessage = await _agentDbContext.ChatMessages.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.IsSummary);

            var messagesToUse = new List<ChatMessage>();

            if (summaryMessage != null)
            {
                var latestMessages = await _agentDbContext.ChatMessages
                                                                .Where(m => m.ConversationId == conversationId)
                                                                .OrderByDescending(m => m.UpdatedAt)
                                                                .Take(MAX_LATEST_MESSAGE_TO_KEEP_FULL + 1)
                                                                .ToListAsync();
                var hasSummaryMessage = latestMessages.Any(c => c.IsSummary);
                var messagesAfterSummary = latestMessages.Where(m => !m.IsSummary && m.UpdatedAt >= summaryMessage.UpdatedAt).ToList();

                if (!hasSummaryMessage || messagesAfterSummary.Count >= MAX_LATEST_MESSAGE_TO_KEEP_FULL)
                {
                    var toResummarize = new List<ChatMessage> { summaryMessage };
                    toResummarize.AddRange(messagesAfterSummary);

                    var summary = await SummarizeMessagesAsync(toResummarize);

                    summaryMessage.Content = $"Summary of earlier conversation: {summary}";
                    summaryMessage.UpdatedAt = DateTime.UtcNow;
                    _agentDbContext.ChatMessages.Update(summaryMessage);
                    await _agentDbContext.SaveChangesAsync();

                    messagesToUse = new List<ChatMessage> { summaryMessage };
                    messagesToUse.AddRange(messagesAfterSummary);
                }
                else
                {
                    messagesToUse = new List<ChatMessage> { summaryMessage };
                    messagesToUse.AddRange(latestMessages);
                }
            }
            else
            {
                var allMessages = await _agentDbContext.ChatMessages
                                                        .Where(m => m.ConversationId == conversationId)
                                                        .OrderByDescending(c => c.UpdatedAt)
                                                        .ToListAsync();
                if (allMessages.Count > MAX_LATEST_MESSAGE_TO_KEEP_FULL)
                {
                    var toSummarize = allMessages.Take(allMessages.Count - MAX_LATEST_MESSAGE_TO_KEEP_FULL).ToList();
                    var summary = await SummarizeMessagesAsync(toSummarize);

                    var systemMessage = new ChatMessage
                    {
                        UserId = userId.Value,
                        Conversation = conversation,
                        Content = $"Summary of earlier conversation: {summary}",
                        Role = ChatRole.System,
                        IsSummary = true
                    };

                    _agentDbContext.ChatMessages.Add(systemMessage);
                    await _agentDbContext.SaveChangesAsync();

                    messagesToUse = new List<ChatMessage> { systemMessage };
                    messagesToUse.AddRange(allMessages.TakeLast(MAX_LATEST_MESSAGE_TO_KEEP_FULL));
                }
                else
                {
                    messagesToUse = allMessages;
                }
            }

            // Stream agent reply
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
                UserId = userId.Value,
                Conversation = conversation,
                Content = promptRequest.Prompt,
                Role = ChatRole.User
            };

            var assistantMessage = new ChatMessage
            {
                UserId = userId.Value,
                Conversation = conversation,
                Content = agentMessageSb.ToString(),
                Role = ChatRole.Assistant
            };

            _agentDbContext.ChatMessages.AddRange(userMessage, assistantMessage);
            await _agentDbContext.SaveChangesAsync();
        }
        else
        {
            await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest.Prompt, null))
            {
                yield return item;
            }
        }
    }

    private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(string prompt, List<ChatMessage>? previousMessages)
    {
        var messages = new List<ChatMessageContent>();
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
                messages.Add(new ChatMessageContent(role, msg.Content));
            }
        }

        messages.Add(new ChatMessageContent(AuthorRole.User, prompt));

        var arguments = new KernelArguments(new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        });

        ChatCompletionAgent agent = new()
        {
            Name = "NTG-Assistant",
            Instructions = @"Do not present speculation, deduction, or hallucination as fact.",
            Kernel = _kernel,
            Arguments = arguments
        };

        var result = agent.InvokeStreamingAsync(messages);
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
