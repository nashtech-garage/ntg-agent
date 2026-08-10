namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>A single bundled file belonging to an imported skill package.</summary>
public record SkillAssetDto(string RelativePath, long SizeBytes);

/// <summary>Full skill record, including the SKILL.md body and bundled assets.</summary>
/// <param name="Replaced">
/// True when this import overwrote an existing skill of the same name rather than creating one.
/// Only meaningful on the response to <c>POST api/skills/import</c>; the read endpoints leave it
/// false. It exists because replacement is otherwise a silent side effect — re-importing looks
/// identical to a first import, while it has in fact discarded the previous body and every asset.
/// </param>
public record SkillDetail(
    Guid Id,
    string Name,
    string Description,
    string Version,
    string ImportedByEmail,
    DateTime CreatedAt,
    int AssetCount,
    string Body,
    IList<SkillAssetDto> Assets,
    bool Replaced = false);
