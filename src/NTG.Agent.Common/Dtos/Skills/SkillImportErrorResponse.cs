namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>Shape of the 400 response body returned by POST api/skills/import when a package fails
/// validation. Each entry is a readable, line-item description of what is wrong with the package.</summary>
public record SkillImportErrorResponse(IList<string> Errors);
