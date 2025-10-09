using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class DateTimePlugin
{
    [Description("Get current datetime")]
    public static DateTime GetCurrentDateTime()
    {
        return DateTime.Now;
    }
}
