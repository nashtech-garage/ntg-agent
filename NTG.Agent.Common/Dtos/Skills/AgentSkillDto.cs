namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>Binding between an agent and a skill it may load. Used for both the GET (with Name/Description
/// populated for display) and the PUT (where only SkillId/IsEnabled are required by the API).</summary>
public class AgentSkillDto
{
    public Guid SkillId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
}
