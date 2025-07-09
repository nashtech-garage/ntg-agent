using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace NTG.Agent.Orchestrator.Agents;

public interface IAgentService
{
    IAsyncEnumerable<string> InvokePromptStreamingAsync(string prompt);
}

public class AgentService (Kernel kernel) : IAgentService
{
    private readonly Kernel _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));

    public async IAsyncEnumerable<string> InvokePromptStreamingAsync(string prompt)
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
                Kernel = kernel,
                Arguments = new KernelArguments(new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })

            };
        var result = agent.InvokeStreamingAsync(prompt);
        await foreach (var item in result)
        {
            yield return item.Message.ToString();
        }
    }
}
