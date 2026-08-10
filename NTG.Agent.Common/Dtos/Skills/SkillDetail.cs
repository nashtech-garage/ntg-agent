namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>A single bundled file belonging to an imported skill package.</summary>
public record SkillAssetDto(string RelativePath, long SizeBytes);

/// <summary>Full skill record, including the SKILL.md body and bundled assets.</summary>
public record SkillDetail(
    Guid Id,
    string Name,
    string Description,
    string Version,
    string ImportedByEmail,
    DateTime CreatedAt,
    int AssetCount,
    string Body,
    IList<SkillAssetDto> Assets);
