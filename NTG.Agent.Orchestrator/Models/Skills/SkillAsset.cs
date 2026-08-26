namespace NTG.Agent.Orchestrator.Models.Skills;

/// <summary>
/// One bundled file from a skill package — anything in the archive other than <c>SKILL.md</c>
/// itself (surface templates under <c>assets/</c>, docs under <c>references/</c>, images).
/// <para>
/// Stored as bytes rather than text because the import allowlist admits binary types (<c>.png</c>).
/// Text assets are UTF-8; use <see cref="ReadText"/> rather than decoding at each call site.
/// </para>
/// </summary>
public class SkillAsset
{
    public SkillAsset()
    {
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid SkillId { get; set; }

    public Skill Skill { get; set; } = null!;

    /// <summary>
    /// Path relative to the skill root, always forward-slashed and never rooted or containing
    /// <c>..</c> — the importer normalizes and rejects anything else before this is written.
    /// Example: <c>assets/trip-search.json</c>.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    /// <summary>Decodes <see cref="Content"/> as UTF-8. Only meaningful for text assets.</summary>
    public string ReadText() => System.Text.Encoding.UTF8.GetString(Content);
}
