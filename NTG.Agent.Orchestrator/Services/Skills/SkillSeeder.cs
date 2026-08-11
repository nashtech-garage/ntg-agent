using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Models.Skills;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Imports the repo's bundled skill packages (<c>seed/skills/*.zip</c>) on startup, through the
/// same <see cref="SkillPackageImporter"/> and <see cref="SkillRegistry"/> an admin upload goes
/// through.
/// </summary>
/// <remarks>
/// <para>
/// The point is one code path, not two. A fresh clone demos without anyone clicking Import, and the
/// importer — the trust boundary for content that becomes model instructions — is exercised on
/// every cold start rather than only when someone happens to upload.
/// </para>
/// <para>
/// Three properties this class exists to guarantee, each of which is easy to lose:
/// </para>
/// <list type="number">
/// <item>
/// <b>It never replaces.</b> <see cref="SkillRegistry.ImportAsync"/> updates a same-named skill in
/// place; running it unconditionally on every boot would silently discard an admin's edits and
/// reset the assets under a running demo. A package whose skill name is already stored is skipped
/// entirely — nothing is even staged for it.
/// </item>
/// <item>
/// <b>It never prevents startup.</b> Serving chat matters more than a demo package being present,
/// so a malformed archive, an absent directory or a database hiccup is logged and stepped over.
/// </item>
/// <item>
/// <b>It never binds.</b> Seeding makes a package available; binding it to an agent is an explicit
/// admin decision and stays one. No <see cref="AgentSkill"/> row is written here.
/// </item>
/// </list>
/// </remarks>
public sealed class SkillSeeder(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<SkillSeeder> logger) : BackgroundService
{
    /// <summary>
    /// Optional absolute or content-root-relative path to the directory holding the seed packages.
    /// </summary>
    /// <remarks>
    /// Configurable because the default is a guess: the seed tree is a repo artifact, and where the
    /// repo sits relative to the process differs between <c>dotnet run</c>, Aspire, a container and
    /// a published bundle. A deployment that does ship the packages can point at them; everything
    /// else keeps the zero-config repo behaviour.
    /// </remarks>
    public const string SeedDirectoryConfigurationKey = "Skills:SeedDirectory";

    /// <summary>Repo-relative location of the packages, used when nothing is configured.</summary>
    private static readonly string[] DefaultSeedPathSegments = ["seed", "skills"];

    /// <summary>
    /// How far above <see cref="IHostEnvironment.ContentRootPath"/> to look for the seed tree.
    /// Two is enough today (<c>&lt;repo&gt;/NTG.Agent.Orchestrator</c>); the extra levels cover a
    /// nested or worktree layout without ever scanning as far as the filesystem root.
    /// </summary>
    private const int MaxAncestorLevels = 6;

    /// <summary>
    /// Recorded as <see cref="Skill.ImportedByUserId"/> for a seeded package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A seeded skill has no human importer, and inventing one would be worse than admitting it:
    /// <see cref="Skill.ImportedByUserId"/> exists to answer "who introduced these instructions",
    /// so resolving some admin account here would attribute a package to a person who never saw it.
    /// </para>
    /// <para>
    /// <see cref="Guid.Empty"/> is safe as the "seeded by the system" marker because real ids come
    /// from ASP.NET Identity's <c>AspNetUsers</c>, which never mints an empty GUID — so the value is
    /// unambiguously not a user. It also degrades correctly in the UI without special-casing:
    /// <see cref="SkillRegistry.ListAsync"/> resolves the importer's email by lookup and yields an
    /// empty string when there is no matching user, so a seeded skill shows a blank importer rather
    /// than someone else's name. The log line at the seeding site says "seeded by the system"
    /// outright, so the marker never has to be decoded from the database alone.
    /// </para>
    /// </remarks>
    public static readonly Guid SeededByUserId = Guid.Empty;

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly IHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ILogger<SkillSeeder> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private enum SeedOutcome
    {
        Seeded,
        AlreadyPresent,
        Failed,
    }

    /// <remarks>
    /// A <see cref="BackgroundService"/> rather than blocking work in <c>StartAsync</c>: the app
    /// begins serving while this runs, which is the right trade for a demo convenience. The blanket
    /// catch is not defensive habit — <c>BackgroundServiceExceptionBehavior</c> defaults to
    /// <c>StopHost</c>, so an exception escaping here would take the whole Orchestrator down.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down mid-seed. Nothing was half-written: each package is one SaveChanges.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skill seeding failed; the Orchestrator continues without seeded skills.");
        }
    }

    /// <summary>
    /// Runs one seeding pass. Public so it can be driven directly in tests, and so a future admin
    /// "re-scan seed packages" action has something to call.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var directory = ResolveSeedDirectory();
        if (directory is null)
        {
            return;
        }

        string[] packages;
        try
        {
            packages = Directory.GetFiles(directory, "*.zip", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Skill seeding: '{Directory}' could not be listed.", directory);
            return;
        }

        if (packages.Length == 0)
        {
            _logger.LogDebug("Skill seeding: no packages in '{Directory}'.", directory);
            return;
        }

        // Deterministic order so logs from two runs line up, and so a package that fails always
        // fails in the same place relative to the others.
        Array.Sort(packages, StringComparer.Ordinal);

        var seeded = 0;
        var present = 0;
        var failed = 0;

        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (await SeedPackageAsync(package, cancellationToken))
            {
                case SeedOutcome.Seeded:
                    seeded++;
                    break;
                case SeedOutcome.AlreadyPresent:
                    present++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        _logger.LogInformation(
            "Skill seeding from '{Directory}': {Seeded} seeded, {AlreadyPresent} already present, {Failed} failed.",
            directory,
            seeded,
            present,
            failed);
    }

    /// <summary>
    /// Reads, identifies and — only if its name is not already stored — imports one package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a parse-first check rather than import-and-roll-back.</b> The zip's file name is not
    /// authoritative — the skill's identity is the <c>name</c> frontmatter — so the name has to come
    /// out of the package. It comes from <see cref="SkillPackageImporter.Import"/>, the very parse
    /// the upload path uses, so the seeder cannot disagree with the importer about what a package is
    /// called (a hand-rolled frontmatter peek would, the first time normalisation or validation
    /// changed). The importer writes nothing by construction — the caller owns the transaction — so
    /// this costs one extra parse of a bounded (≤ <see cref="SkillPackageImporter.MaxPackageBytes"/>)
    /// archive per cold start and touches no database state.
    /// </para>
    /// <para>
    /// Import-and-roll-back was the alternative and is strictly more dangerous here: it would need
    /// an explicit transaction (this codebase opens none anywhere), and because
    /// <see cref="SkillRegistry.ImportAsync"/> replaces in place by deleting the existing asset rows,
    /// a rollback that failed to take would destroy exactly what this seeder exists to protect.
    /// Never staging the write cannot fail that way.
    /// </para>
    /// </remarks>
    private async Task<SeedOutcome> SeedPackageAsync(string path, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(path);

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

            // A scope per package, not one for the run. SkillRegistry is registered AddScoped, so a
            // scope is required at all here; a fresh one per package additionally contains failure —
            // after a failed SaveChangesAsync EF still tracks the rejected entity as Added, and a
            // shared DbContext would replay it into every later package's save.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var importer = scope.ServiceProvider.GetRequiredService<SkillPackageImporter>();
            var registry = scope.ServiceProvider.GetRequiredService<SkillRegistry>();

            var parsed = importer.Import(bytes, fileName, SeededByUserId);
            if (!parsed.Succeeded)
            {
                _logger.LogWarning(
                    "Skill seeding: package '{FileName}' was rejected and will not be seeded — {Errors}",
                    fileName,
                    string.Join(" | ", parsed.Errors));

                return SeedOutcome.Failed;
            }

            var name = parsed.Skill!.Name;

            // OrdinalIgnoreCase mirrors SQL Server's default case-insensitive collation on the
            // unique index: a name that differs only in case is the same row to the database, and
            // attempting it would be a guaranteed collision rather than a new skill.
            var stored = await registry.ListAsync(cancellationToken);
            if (stored.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug(
                    "Skill seeding: '{SkillName}' is already in the database; leaving the stored copy untouched.",
                    name);

                return SeedOutcome.AlreadyPresent;
            }

            var outcome = await registry.ImportAsync(bytes, fileName, SeededByUserId, cancellationToken);

            if (!outcome.Succeeded)
            {
                _logger.LogWarning(
                    "Skill seeding: package '{FileName}' was rejected on import — {Errors}",
                    fileName,
                    string.Join(" | ", outcome.Errors));

                return SeedOutcome.Failed;
            }

            if (outcome.Replaced)
            {
                // Unreachable via the check above unless something raced us between the read and the
                // write. Worth a warning rather than silence: it means a stored skill was overwritten.
                _logger.LogWarning(
                    "Skill seeding: '{SkillName}' replaced an existing skill; it was absent moments earlier.",
                    outcome.Name);
            }

            _logger.LogInformation(
                "Skill seeding: '{SkillName}' seeded from '{FileName}' by the system "
                + "(no human importer; ImportedByUserId={ImportedByUserId}). Not bound to any agent.",
                outcome.Name,
                fileName,
                SeededByUserId);

            return SeedOutcome.Seeded;
        }
        catch (DbUpdateException ex)
        {
            // Two Orchestrator instances starting together can both find the skill absent and both
            // insert it; the unique index on Skills.Name resolves that for us. Losing the race is a
            // correct outcome — the skill is present, which is all the seeder wanted — so this is
            // reported, not retried, and deliberately buys no locking infrastructure for a demo.
            _logger.LogInformation(
                ex,
                "Skill seeding: '{FileName}' was not stored, most likely because another instance "
                + "seeded it concurrently. The skill should already be present.",
                fileName);

            return SeedOutcome.AlreadyPresent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Skill seeding: package '{FileName}' could not be seeded.", fileName);
            return SeedOutcome.Failed;
        }
    }

    /// <summary>
    /// Finds the seed directory, or <see langword="null"/> when there is nothing to seed.
    /// </summary>
    /// <remarks>
    /// The path is repo-relative and simply will not exist in a published or container build, which
    /// is a normal state and not a fault — hence "no directory" is a silent no-op rather than a
    /// warning. An explicitly configured path that is missing <em>is</em> warned about: somebody
    /// asked for it by name.
    /// </remarks>
    private string? ResolveSeedDirectory()
    {
        var configured = _configuration[SeedDirectoryConfigurationKey];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolved = Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));

            if (Directory.Exists(resolved))
            {
                return resolved;
            }

            _logger.LogWarning(
                "Skill seeding: configured {ConfigurationKey} '{Configured}' resolved to '{Resolved}', "
                + "which does not exist. Nothing will be seeded.",
                SeedDirectoryConfigurationKey,
                configured,
                resolved);

            return null;
        }

        // Walk up from the content root rather than assuming a fixed depth: under Aspire the
        // content root is the project directory (the same assumption Program.cs's DataProtection
        // `../key/` already relies on), under `dotnet run` from elsewhere it may not be, and in a
        // container no ancestor has a seed tree at all — which ends the walk empty.
        var current = new DirectoryInfo(_environment.ContentRootPath);

        for (var level = 0; current is not null && level <= MaxAncestorLevels; level++, current = current.Parent)
        {
            var candidate = Path.Combine([current.FullName, .. DefaultSeedPathSegments]);

            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        _logger.LogDebug(
            "Skill seeding: no '{SeedPath}' directory at or above '{ContentRoot}'; nothing to seed. "
            + "This is expected outside a repo checkout. Set {ConfigurationKey} to seed from elsewhere.",
            string.Join('/', DefaultSeedPathSegments),
            _environment.ContentRootPath,
            SeedDirectoryConfigurationKey);

        return null;
    }
}
