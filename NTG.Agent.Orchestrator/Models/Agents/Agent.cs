namespace NTG.Agent.Orchestrator.Models.Agents;

public class Agent
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;
}
