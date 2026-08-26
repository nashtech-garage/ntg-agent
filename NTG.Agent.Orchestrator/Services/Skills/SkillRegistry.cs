using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Skills;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Persistence for imported Agent Skills: import (with replace-on-reimport), list, read and delete.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SkillPackageImporter"/> validates and builds the entity graph but writes nothing; this
/// class owns the database side of that split, so a rejected package cannot leave partial rows
/// behind. Everything a single import touches is staged and committed in one
/// <c>SaveChangesAsync</c>, which EF wraps in a transaction — the codebase uses no explicit
/// <c>BeginTransactionAsync</c> anywhere, and one save is sufficient here.
/// </para>
/// <para>
/// Read paths deliberately project rather than loading entities. <see cref="SkillAsset.Content"/> is
/// a <c>varbinary(max)</c>, so a listing that materialised <see cref="Skill"/> graphs would pull
/// every asset blob of every skill into memory to render a table of names.
/// </para>
/// </remarks>
public sealed class SkillRegistry(
    AgentDbContext dbContext,
    SkillPackageImporter importer,
    ILogger<SkillRegistry> logger)
{
    private readonly AgentDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly SkillPackageImporter _importer = importer ?? throw new ArgumentNullException(nameof(importer));
    private readonly ILogger<SkillRegistry> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Import result: the stored skill, or the reasons the package was rejected.</summary>
    public sealed record ImportOutcome(Guid? SkillId, string? Name, IReadOnlyList<string> Errors, bool Replaced)
    {
        public bool Succeeded => SkillId is not null;
    }

    /// <summary>One row of the skills list. Carries no asset bytes.</summary>
    public sealed record SkillSummary(
        Guid Id,
        string Name,
        string Description,
        string? Version,
        string ImportedByEmail,
        int AssetCount,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    /// <summary>A skill with its asset paths and sizes, but not their bytes.</summary>
    /// <remarks>
    /// <see cref="AssetCount"/> duplicates <c>Assets.Count</c> deliberately: the wire DTO carries
    /// both, and the import response reuses this shape, so leaving it to be inferred would send a
    /// zero the client renders verbatim.
    /// </remarks>
    public sealed record SkillDetailView(
        Guid Id,
        string Name,
        string Description,
        string Body,
        string? Version,
        string? License,
        string? Compatibility,
        string? SourceFileName,
        string ImportedByEmail,
        int AssetCount,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<SkillAssetView> Assets);

    public sealed record SkillAssetView(string RelativePath, int SizeBytes);

    /// <summary>
    /// Validates and stores a package. When a skill of the same name already exists it is updated
    /// in place and its assets replaced.
    /// </summary>
    /// <remarks>
    /// The existing row's <see cref="Skill.Id"/> is deliberately preserved rather than deleting and
    /// re-inserting. <see cref="AgentSkill"/> rows reference it, so a delete-and-insert would
    /// silently unbind the skill from every agent using it — turning "re-upload a fixed version"
    /// into "re-upload, then remember to re-bind everywhere", which is exactly the step someone
    /// forgets before a demo.
    /// </remarks>
    public async Task<ImportOutcome> ImportAsync(
        byte[] packageBytes, string fileName, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = _importer.Import(packageBytes, fileName, userId);

        if (!result.Succeeded)
        {
            _logger.LogInformation(
                "Skill import from '{FileName}' rejected with {ErrorCount} error(s)",
                fileName,
                result.Errors.Count);

            return new ImportOutcome(null, null, result.Errors, Replaced: false);
        }

        var imported = result.Skill!;

        var existing = await _dbContext.Skills
            .Include(s => s.Assets)
            .FirstOrDefaultAsync(s => s.Name == imported.Name, cancellationToken);

        var replaced = existing is not null;

        if (existing is not null)
        {
            existing.Description = imported.Description;
            existing.Body = imported.Body;
            existing.Version = imported.Version;
            existing.License = imported.License;
            existing.Compatibility = imported.Compatibility;
            existing.SourceFileName = imported.SourceFileName;
            existing.ImportedByUserId = imported.ImportedByUserId;
            existing.UpdatedAt = DateTime.UtcNow;

            _dbContext.SkillAssets.RemoveRange(existing.Assets);

            foreach (var asset in imported.Assets)
            {
                asset.SkillId = existing.Id;
                asset.Skill = null!;
                _dbContext.SkillAssets.Add(asset);
            }
        }
        else
        {
            _dbContext.Skills.Add(imported);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var skillId = existing?.Id ?? imported.Id;

        // Provenance. Skills are replaced in place, so without a digest of the exact instructions
        // that were stored, "which version of this skill was in context that day" is unanswerable
        // from the logs alone.
        _logger.LogInformation(
            "Skill '{SkillName}' {Action} by {UserId} from '{FileName}' ({AssetCount} asset(s), body sha256:{BodyHash})",
            imported.Name,
            replaced ? "replaced" : "imported",
            userId,
            fileName,
            imported.Assets.Count,
            Sha256(imported.Body));

        return new ImportOutcome(skillId, imported.Name, [], replaced);
    }

    public async Task<IReadOnlyList<SkillSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Skills
            .OrderBy(s => s.Name)
            .Select(s => new SkillSummary(
                s.Id,
                s.Name,
                s.Description,
                s.Version,
                _dbContext.Users
                    .Where(u => u.Id == s.ImportedByUserId)
                    .Select(u => u.Email)
                    .FirstOrDefault() ?? string.Empty,
                s.Assets.Count,
                s.CreatedAt,
                s.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<SkillDetailView?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Skills
            .Where(s => s.Id == id)
            .Select(s => new SkillDetailView(
                s.Id,
                s.Name,
                s.Description,
                s.Body,
                s.Version,
                s.License,
                s.Compatibility,
                s.SourceFileName,
                _dbContext.Users
                    .Where(u => u.Id == s.ImportedByUserId)
                    .Select(u => u.Email)
                    .FirstOrDefault() ?? string.Empty,
                s.Assets.Count,
                s.CreatedAt,
                s.UpdatedAt,
                s.Assets
                    .OrderBy(a => a.RelativePath)
                    .Select(a => new SkillAssetView(a.RelativePath, a.Content.Length))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>One bound, enabled skill as seen by a running agent.</summary>
    public sealed record ActiveSkill(Guid Id, string Name, string Description);

    /// <summary>
    /// The skills an agent may use in a run: bound <em>and</em> enabled, name and description only.
    /// </summary>
    /// <remarks>
    /// Bodies are excluded on purpose. This is tier 1 of the spec's progressive disclosure — the
    /// catalog is injected on every run for every bound skill, so putting bodies here would spend
    /// the entire context budget on instructions the model has not asked for and may not need.
    /// </remarks>
    public async Task<IReadOnlyList<ActiveSkill>> GetActiveSkillsAsync(
        Guid agentId, CancellationToken cancellationToken = default) =>
        await _dbContext.AgentSkills
            .Where(b => b.AgentId == agentId && b.IsEnabled)
            .OrderBy(b => b.Skill.Name)
            .Select(b => new ActiveSkill(b.SkillId, b.Skill.Name, b.Skill.Description))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The <c>SKILL.md</c> body of a bound, enabled skill, or <see langword="null"/>. Tier 2 —
    /// fetched only when the model activates the skill.
    /// </summary>
    public async Task<string?> GetActiveSkillBodyAsync(
        Guid agentId, string skillName, CancellationToken cancellationToken = default) =>
        await _dbContext.AgentSkills
            .Where(b => b.AgentId == agentId && b.IsEnabled && b.Skill.Name == skillName)
            .Select(b => b.Skill.Body)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// A named asset belonging to a bound, enabled skill, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The binding is re-checked here rather than trusted from the caller: this is what stops a
    /// model from naming a skill that was never bound to the agent it is running as.
    /// </remarks>
    public async Task<byte[]?> GetActiveSkillAssetAsync(
        Guid agentId, string skillName, string relativePath, CancellationToken cancellationToken = default) =>
        await _dbContext.AgentSkills
            .Where(b => b.AgentId == agentId && b.IsEnabled && b.Skill.Name == skillName)
            .SelectMany(b => b.Skill.Assets)
            .Where(a => a.RelativePath == relativePath)
            .Select(a => a.Content)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Every skill in the system, each flagged with whether it is bound and enabled for
    /// <paramref name="agentId"/>.
    /// </summary>
    /// <remarks>
    /// Returns all skills rather than only bound ones, mirroring
    /// <c>AgentAdminController.GetInnerAgentBindings</c>: the caller renders one toggle per row, so
    /// returning only the bound ones would make an unbound skill impossible to bind.
    /// </remarks>
    public async Task<IReadOnlyList<(Guid SkillId, string Name, string Description, bool IsEnabled)>>
        GetAgentBindingsAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var skills = await _dbContext.Skills
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Description })
            .ToListAsync(cancellationToken);

        var enabled = await _dbContext.AgentSkills
            .Where(b => b.AgentId == agentId)
            .ToDictionaryAsync(b => b.SkillId, b => b.IsEnabled, cancellationToken);

        return skills
            .Select(s => (
                s.Id,
                s.Name,
                s.Description,
                enabled.TryGetValue(s.Id, out var isEnabled) && isEnabled))
            .ToList();
    }

    /// <summary>
    /// Applies the requested bindings for an agent. Returns false when any skill id does not exist.
    /// </summary>
    public async Task<bool> SetAgentBindingsAsync(
        Guid agentId,
        IReadOnlyList<(Guid SkillId, bool IsEnabled)> bindings,
        CancellationToken cancellationToken = default)
    {
        var requested = bindings.Select(b => b.SkillId).Distinct().ToList();

        var known = await _dbContext.Skills
            .Where(s => requested.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (known.Count != requested.Count)
        {
            return false;
        }

        var existing = await _dbContext.AgentSkills
            .Where(b => b.AgentId == agentId)
            .ToDictionaryAsync(b => b.SkillId, cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var (skillId, isEnabled) in bindings)
        {
            if (existing.TryGetValue(skillId, out var binding))
            {
                binding.IsEnabled = isEnabled;
                binding.UpdatedAt = now;
            }
            else
            {
                _dbContext.AgentSkills.Add(new AgentSkill
                {
                    AgentId = agentId,
                    SkillId = skillId,
                    IsEnabled = isEnabled,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Loads a skill with its asset bytes. Only for export — every other read projects.</summary>
    public async Task<Skill?> GetForExportAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Skills
            .Include(s => s.Assets)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    /// <summary>
    /// Deletes a skill, its assets and its agent bindings. Returns false when it does not exist.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var skill = await _dbContext.Skills
            .Include(s => s.Assets)
            .Include(s => s.AgentBindings)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (skill is null)
        {
            return false;
        }

        _dbContext.SkillAssets.RemoveRange(skill.Assets);
        _dbContext.AgentSkills.RemoveRange(skill.AgentBindings);
        _dbContext.Skills.Remove(skill);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Skill '{SkillName}' ({SkillId}) deleted", skill.Name, id);
        return true;
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
