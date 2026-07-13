namespace NTG.Agent.MCP.Server.Models;

/// <summary>
/// An Agent Skill stored as a SKILL.md document (agentskills.io format).
/// Name and Description are denormalized from the YAML frontmatter for listing
/// and for building the skill://index.json discovery document.
/// </summary>
public class Skill
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Full SKILL.md text, frontmatter included.</summary>
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
