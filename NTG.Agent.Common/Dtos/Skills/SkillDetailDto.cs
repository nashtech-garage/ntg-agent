namespace NTG.Agent.Common.Dtos.Skills;

public class SkillDetailDto : SkillDto
{
    /// <summary>Full SKILL.md text, frontmatter included.</summary>
    public string Content { get; set; } = string.Empty;
}
