using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
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

            ChatMessage userMessage = new()
            {
                UserId = userId.Value,
                Conversation = conversation,
                Content = promptRequest.Prompt,
                Role = ChatRole.User,
                CreatedAt = DateTime.UtcNow
            };

            StringBuilder agentMessageSb = new StringBuilder();
            await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest.Prompt))
            {
                agentMessageSb.Append(item);
                yield return item;
            }

            if (conversation.Name == "New Conversation")
            {
                conversation.Name = await GenerateConversationName(promptRequest.Prompt);
                _agentDbContext.Conversations.Update(conversation);
            }

            ChatMessage agentMessage = new()
            {
                UserId = userId.Value,
                Conversation = conversation,
                Content = agentMessageSb.ToString(),
                Role = ChatRole.Assistant,
                CreatedAt = DateTime.UtcNow
            };
            _agentDbContext.ChatMessages.Add(userMessage);
            _agentDbContext.ChatMessages.Add(agentMessage);
            await _agentDbContext.SaveChangesAsync();
        }
        else
        {
            await foreach (var item in InvokePromptStreamingInternalAsync(promptRequest.Prompt))
            {
                yield return item;
            }
        }
    }

    private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(string prompt)
    {
        ChatCompletionAgent agent =
            new()
            {
                Name = "NTG-Assistant",
                Instructions = @"Do not present speculation, deduction, or hallucination as fact.",
                Kernel = _kernel,
                Arguments = new KernelArguments(new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
            };
        var result = agent.InvokeStreamingAsync(prompt);
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
}
