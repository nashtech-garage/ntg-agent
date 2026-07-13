namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>Public listing of an agent's enabled skills, used by the /skill picker in the web client.</summary>
public record AgentSkillListItemDto(string Name, string Description);
