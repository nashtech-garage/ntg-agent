namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>Summary row shown in the Skills list/grid.</summary>
public record SkillListItem(
    Guid Id,
    string Name,
    string Description,
    string Version,
    string ImportedByEmail,
    DateTime CreatedAt,
    int AssetCount);
