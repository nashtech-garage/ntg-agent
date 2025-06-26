using Microsoft.SemanticKernel;

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
        var kernelSetting = new PromptExecutionSettings
        {
               ServiceId = "github"
        };

        var kernelArguments = new KernelArguments(kernelSetting);
        var result = _kernel.InvokePromptStreamingAsync(prompt, kernelArguments);
        await foreach (var item in result)
        {
            yield return item.ToString();
        }
    }
}
