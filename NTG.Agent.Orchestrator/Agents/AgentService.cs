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
                Name = "SK-Assistant",
                Instructions = @"Do not present speculation, deduction, or hallucination as fact.
                                • If unverified, say:
                                  - “I cannot verify this.”
                                  - “I do not have access to that information.”
                                • Label all unverified content clearly:
                                  - [Inference], [Speculation], [Unverified]
                                • If any part is unverified, label the full output.
                                • Ask instead of assuming.
                                • Never override user facts, labels, or data.
                                • Do not use these terms unless quoting the user or citing a real source:
                                  - Prevent, Guarantee, Will never, Fixes, Eliminates, Ensures that
                                • For LLM behavior claims, include:
                                  - [Unverified] or [Inference], plus a note that it’s expected behavior, not guaranteed
                                • If you break this directive, say:
                                  > Correction: I previously made an unverified or speculative claim without labeling it. That was an error.",
                Kernel = _kernel,
                Arguments = new KernelArguments(new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
            };
        var result = agent.InvokeStreamingAsync(prompt);
        await foreach (var item in result)
        {
            yield return item.Message.ToString();
        }
    }
}
