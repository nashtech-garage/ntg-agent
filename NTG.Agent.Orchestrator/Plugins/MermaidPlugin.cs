using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public class MermaidPlugin
{
    [KernelFunction("create_diagram")]
    [Description("Creates a Mermaid diagram")]
    public string CreateDiagram([Description("What to diagram")] string description)
    {
        return $"Create a Mermaid diagram for: {description}";
    }
}
