namespace NTG.Agent.Common.Dtos.Skills;

/// <summary>
/// Create/update payload for a skill. Name and description are parsed server-side
/// from the SKILL.md frontmatter, so the content is the whole contract.
/// </summary>
public class SaveSkillRequest
{
    public string Content { get; set; } = string.Empty;
}
