using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class DateTimePlugin
{
    [KernelFunction, Description("Get current datetime")]
    public DateTime GetCurrentDateTime()
    {
        return DateTime.Now;
    }
}
