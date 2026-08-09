namespace NTG.Agent.Orchestrator.Models.Skills;

/// <summary>
/// An imported Agent Skill (<see href="https://agentskills.io"/>): the parsed <c>SKILL.md</c>
/// frontmatter and body, plus any bundled files in <see cref="Assets"/>. Skills are uploaded as
/// <c>.zip</c> packages through the admin API and bound to agents via <see cref="AgentSkill"/>.
/// <para>
/// Loading follows the spec's progressive disclosure: <see cref="Name"/> + <see cref="Description"/>
/// go into the catalog at session start, and <see cref="Body"/> is read only when the skill is
/// activated. See <c>docs/Agent-Skills-Implementation-Plan.md</c>.
/// </para>
/// </summary>
public class Skill
{
    public Skill()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    /// <summary>
    /// Spec <c>name</c> frontmatter: 1-64 characters, lowercase alphanumerics and hyphens only,
    /// no leading/trailing/consecutive hyphens, and matching the package's directory name.
    /// Unique across skills — re-importing the same name replaces the existing skill.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Spec <c>description</c> frontmatter, max 1024 characters. This is the only skill content
    /// loaded at session start, so it alone decides whether the model activates the skill.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The <c>SKILL.md</c> markdown body, frontmatter stripped. Loaded on activation.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Optional <c>metadata.version</c> from frontmatter.</summary>
    public string? Version { get; set; }

    /// <summary>Optional spec <c>license</c> frontmatter.</summary>
    public string? License { get; set; }

    /// <summary>Optional spec <c>compatibility</c> frontmatter, max 500 characters.</summary>
    public string? Compatibility { get; set; }

    /// <summary>Original uploaded file name, retained for provenance alongside <see cref="ImportedByUserId"/>.</summary>
    public string? SourceFileName { get; set; }

    /// <summary>
    /// The admin who imported this package. A skill's body is injected into the model's context,
    /// so provenance matters: this records who introduced those instructions.
    /// </summary>
    public Guid ImportedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Bundled files (references, assets) keyed by path relative to the skill root.</summary>
    public ICollection<SkillAsset> Assets { get; set; } = new List<SkillAsset>();

    /// <summary>Agents this skill is bound to.</summary>
    public ICollection<AgentSkill> AgentBindings { get; set; } = new List<AgentSkill>();
}
