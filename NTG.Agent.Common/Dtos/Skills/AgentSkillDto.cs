namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>Per-agent skill enablement row shown in the Admin UI.</summary>
public class AgentSkillDto
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
}
