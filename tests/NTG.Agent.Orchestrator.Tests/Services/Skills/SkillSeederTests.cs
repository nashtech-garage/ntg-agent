using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Services.Skills;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services.Skills;

/// <summary>
/// Startup seeding behaviour for <see cref="SkillSeeder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The seeder runs unattended on every cold start, which is what makes its failure modes worth
/// testing rather than merely reading. Two of them are silent by nature: re-importing an
/// already-present skill would replace it in place — discarding an admin's edits and resetting its
/// assets — and nothing would surface until someone noticed their change had reverted, possibly
/// several restarts later. The second, an exception escaping the hosted service, is not silent at
/// all: <c>BackgroundServiceExceptionBehavior</c> defaults to <c>StopHost</c>, so a corrupt demo zip
/// would stop the Orchestrator from serving chat.
/// </para>
/// <para>
/// The service provider here is built with <c>ValidateScopes</c>, so every test also asserts the
/// seeder resolves <see cref="SkillRegistry"/> — registered <c>AddScoped</c> in <c>Program.cs</c> —
/// from a scope of its own. Resolving it from the root provider would throw rather than quietly
/// working, which is what would happen in production only after the first request captured a
/// disposed context.
/// </para>
/// </remarks>
[TestFixture]
public class SkillSeederTests
{
    private const string ManifestV1 = """
        ---
        name: demo-skill
        description: First version of the seeded demonstration skill.
        metadata:
          version: "1.0"
        ---

        # Demo v1
        """;

    private const string ManifestV2 = """
        ---
        name: demo-skill
        description: Second version of the seeded demonstration skill.
        metadata:
          version: "2.0"
        ---

        # Demo v2
        """;

    private string _root = null!;
    private string _contentRoot = null!;
    private string _seedDirectory = null!;
    private string _databaseName = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void Setup()
    {
        // A repo-shaped temp tree: <root>/seed/skills alongside <root>/NTG.Agent.Orchestrator, the
        // latter standing in for the content root a running Orchestrator reports.
        _root = Path.Combine(Path.GetTempPath(), "ntg-skill-seeder", Guid.NewGuid().ToString("N"));
        _contentRoot = Path.Combine(_root, "NTG.Agent.Orchestrator");
        _seedDirectory = Path.Combine(_root, "seed", "skills");

        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(_seedDirectory);

        _databaseName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AgentDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        // Mirrors Program.cs exactly: importer singleton, registry scoped.
        services.AddSingleton<SkillPackageImporter>();
        services.AddScoped<SkillRegistry>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }

    /// <summary>A context onto the same in-memory store, for asserting outside the seeder's scopes.</summary>
    private AgentDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AgentDbContext>().UseInMemoryDatabase(_databaseName).Options);

    /// <summary>
    /// Builds a seeder. <paramref name="configuredDirectory"/> mirrors the
    /// <c>Skills:SeedDirectory</c> setting; passing null exercises the walk-up from the content root.
    /// </summary>
    private SkillSeeder NewSeeder(string? configuredDirectory)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(_contentRoot);

        var settings = new Dictionary<string, string?>();
        if (configuredDirectory is not null)
        {
            settings[SkillSeeder.SeedDirectoryConfigurationKey] = configuredDirectory;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new SkillSeeder(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            environment.Object,
            configuration,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SkillSeeder>.Instance);
    }

    private static byte[] Package(string manifest, params (string Path, string Content)[] assets)
    {
        var builder = new TestZipBuilder().AddFile("demo-skill/SKILL.md", manifest);

        foreach (var (path, content) in assets)
        {
            builder.AddFile($"demo-skill/{path}", content);
        }

        return builder.Build();
    }

    private void WritePackage(string fileName, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(_seedDirectory, fileName), bytes);

    /// <summary>Runs one pass the way the host does, so the hosted-service plumbing is under test too.</summary>
    private static async Task RunAsHostedServiceAsync(SkillSeeder seeder)
    {
        await seeder.StartAsync(CancellationToken.None);
        await (seeder.ExecuteTask ?? Task.CompletedTask);
    }

    // ---------------------------------------------------------------- seeding an absent package

    [Test]
    public async Task SeedAsync_PackageNotYetStored_ImportsItWithItsAssets()
    {
        WritePackage("demo-skill.zip", Package(ManifestV1, ("references/guide.md", "guide body")));

        await NewSeeder(_seedDirectory).SeedAsync();

        await using var context = NewContext();
        var stored = await context.Skills.Include(s => s.Assets).SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.Name, Is.EqualTo("demo-skill"));
            Assert.That(stored.Description, Does.StartWith("First version"));
            Assert.That(stored.Version, Is.EqualTo("1.0"));
            Assert.That(stored.SourceFileName, Is.EqualTo("demo-skill.zip"));
            Assert.That(stored.Assets.Select(a => a.RelativePath), Is.EqualTo(new[] { "references/guide.md" }));
        });
    }

    /// <summary>
    /// Provenance. <c>ImportedByUserId</c> answers "who introduced these instructions", and a seeded
    /// package has no human importer — so it must record a value no real account can hold rather
    /// than borrowing an admin's identity.
    /// </summary>
    [Test]
    public async Task SeedAsync_RecordsTheSystemMarkerAsTheImporter()
    {
        WritePackage("demo-skill.zip", Package(ManifestV1));

        await NewSeeder(_seedDirectory).SeedAsync();

        await using var context = NewContext();
        var stored = await context.Skills.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.ImportedByUserId, Is.EqualTo(SkillSeeder.SeededByUserId));
            Assert.That(
                stored.ImportedByUserId,
                Is.EqualTo(Guid.Empty),
                "the marker must stay a value ASP.NET Identity can never issue, so it cannot collide with a user");
        });
    }

    /// <summary>
    /// Seeding makes a package available; binding it to an agent is an explicit admin decision. A
    /// seeder that bound its skills would silently change what every existing agent can do on
    /// upgrade.
    /// </summary>
    [Test]
    public async Task SeedAsync_DoesNotBindTheSkillToAnyAgent()
    {
        await using (var setup = NewContext())
        {
            setup.Agents.Add(new AgentModel { Id = Guid.NewGuid(), Name = "Test Agent", OwnerUserId = Guid.NewGuid() });
            await setup.SaveChangesAsync();
        }

        WritePackage("demo-skill.zip", Package(ManifestV1));

        await NewSeeder(_seedDirectory).SeedAsync();

        await using var context = NewContext();

        Assert.Multiple(() =>
        {
            Assert.That(context.Skills.Count(), Is.EqualTo(1));
            Assert.That(context.AgentSkills.Count(), Is.Zero);
        });
    }

    // ---------------------------------------------------------------- skipping a present package

    /// <summary>
    /// The test this class exists for. <see cref="SkillRegistry.ImportAsync"/> replaces a same-named
    /// skill in place, so a seeder that imported unconditionally would overwrite an admin's edited
    /// body and drop their assets on every single restart — invisibly, and repeatedly. The stored
    /// copy is hand-edited first precisely so "it did not replace" is asserted rather than "the
    /// contents happen to match".
    /// </summary>
    [Test]
    public async Task SeedAsync_SkillNameAlreadyStored_LeavesTheStoredCopyUntouched()
    {
        var adminUserId = Guid.NewGuid();

        await using (var setup = NewContext())
        {
            setup.Users.Add(new User { Id = adminUserId, UserName = "admin", Email = "admin@test.com" });
            await setup.SaveChangesAsync();
        }

        await using (var scope = _provider.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<SkillRegistry>();
            var outcome = await registry.ImportAsync(
                Package(ManifestV1, ("references/guide.md", "original guide")), "demo-skill.zip", adminUserId);

            Assert.That(outcome.Succeeded, Is.True, string.Join(" | ", outcome.Errors));
        }

        await using (var edit = NewContext())
        {
            var skill = await edit.Skills.SingleAsync();
            skill.Body = "# Demo v1\n\nHand-edited by an admin after import.";
            await edit.SaveChangesAsync();
        }

        // A genuinely different package under the same skill name: if the seeder re-imports, every
        // assertion below flips.
        WritePackage("demo-skill.zip", Package(ManifestV2, ("references/replacement.md", "new guide")));

        await NewSeeder(_seedDirectory).SeedAsync();

        await using var context = NewContext();
        var stored = await context.Skills.Include(s => s.Assets).SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(context.Skills.Count(), Is.EqualTo(1));
            Assert.That(stored.Body, Does.Contain("Hand-edited by an admin"), "the admin's edit was overwritten");
            Assert.That(stored.Description, Does.StartWith("First version"));
            Assert.That(stored.Version, Is.EqualTo("1.0"));
            Assert.That(
                stored.ImportedByUserId,
                Is.EqualTo(adminUserId),
                "re-seeding would rewrite provenance to the system marker");
            Assert.That(
                stored.Assets.Select(a => a.RelativePath),
                Is.EqualTo(new[] { "references/guide.md" }),
                "re-seeding replaces the asset set wholesale");
        });
    }

    /// <summary>
    /// Identity comes from the <c>name</c> frontmatter, not the file name: a package renamed on disk
    /// is still the same skill, and re-seeding it would replace the stored copy.
    /// </summary>
    [Test]
    public async Task SeedAsync_SameSkillNameUnderADifferentFileName_IsStillSkipped()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<SkillRegistry>();
            await registry.ImportAsync(Package(ManifestV1), "demo-skill.zip", Guid.NewGuid());
        }

        WritePackage("renamed-on-disk.zip", Package(ManifestV2));

        await NewSeeder(_seedDirectory).SeedAsync();

        await using var context = NewContext();
        var stored = await context.Skills.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(context.Skills.Count(), Is.EqualTo(1));
            Assert.That(stored.Description, Does.StartWith("First version"));
            Assert.That(stored.SourceFileName, Is.EqualTo("demo-skill.zip"));
        });
    }

    // ---------------------------------------------------------------- failure containment

    /// <summary>
    /// A corrupt or hostile package is a logged skip, never an exception. The Orchestrator serving
    /// chat matters more than any demo package, and the remaining packages must still be seeded —
    /// otherwise one bad file silently costs every skill that sorts after it.
    /// </summary>
    [Test]
    public async Task SeedAsync_MalformedPackage_IsSkippedAndTheOthersStillSeed()
    {
        // Sorts before "demo-skill.zip", so a seeder that aborted on the first failure would seed nothing.
        WritePackage("broken.zip", "this is not a zip archive at all"u8.ToArray());
        WritePackage("demo-skill.zip", Package(ManifestV1));

        Assert.DoesNotThrowAsync(async () => await NewSeeder(_seedDirectory).SeedAsync());

        await using var context = NewContext();

        Assert.That(await context.Skills.Select(s => s.Name).ToListAsync(), Is.EqualTo(new[] { "demo-skill" }));
    }

    /// <summary>A package rejected by validation must leave nothing behind, exactly as on upload.</summary>
    [Test]
    public async Task SeedAsync_PackageWithoutAManifest_PersistsNothing()
    {
        WritePackage("no-manifest.zip", new TestZipBuilder().AddFile("demo-skill/notes.md", "no manifest").Build());

        Assert.DoesNotThrowAsync(async () => await NewSeeder(_seedDirectory).SeedAsync());

        await using var context = NewContext();

        Assert.Multiple(() =>
        {
            Assert.That(context.Skills.Count(), Is.Zero);
            Assert.That(context.SkillAssets.Count(), Is.Zero);
        });
    }

    /// <summary>
    /// The hosted-service entry point must contain anything the pass throws.
    /// <c>BackgroundServiceExceptionBehavior</c> defaults to <c>StopHost</c>, so an escaping
    /// exception here does not merely skip seeding — it stops the Orchestrator. The content root is
    /// used as the fault injection point because it is read before any per-package handling.
    /// </summary>
    [Test]
    public async Task ExecuteAsync_WhenSeedingFailsOutright_DoesNotFaultTheHost()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Throws(new IOException("content root is unavailable"));

        var seeder = new SkillSeeder(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            environment.Object,
            new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SkillSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);
        var pass = seeder.ExecuteTask ?? Task.CompletedTask;

        Assert.DoesNotThrowAsync(async () => await pass);
        Assert.That(pass.IsFaulted, Is.False);
    }

    // ---------------------------------------------------------------- locating the seed directory

    /// <summary>
    /// The seed tree is a repo artifact and simply is not there in a published or container build.
    /// That is a normal state, not an error: nothing to seed, nothing to say, nothing thrown.
    /// </summary>
    [Test]
    public async Task SeedAsync_ConfiguredDirectoryMissing_IsANoOp()
    {
        var missing = Path.Combine(_root, "no", "such", "directory");

        Assert.DoesNotThrowAsync(async () => await NewSeeder(missing).SeedAsync());

        await using var context = NewContext();
        Assert.That(context.Skills.Count(), Is.Zero);
    }

    /// <summary>The unconfigured equivalent: no seed tree anywhere above the content root.</summary>
    [Test]
    public async Task SeedAsync_NoSeedDirectoryAboveContentRoot_IsANoOp()
    {
        Directory.Delete(Path.Combine(_root, "seed"), recursive: true);

        Assert.DoesNotThrowAsync(async () => await NewSeeder(configuredDirectory: null).SeedAsync());

        await using var context = NewContext();
        Assert.That(context.Skills.Count(), Is.Zero);
    }

    /// <summary>
    /// With nothing configured the directory is found by walking up from the content root, which is
    /// what makes a fresh clone seed itself: under Aspire the content root is the project directory,
    /// one level below the repo root that holds <c>seed/skills</c>.
    /// </summary>
    [Test]
    public async Task SeedAsync_NoConfiguration_FindsTheSeedDirectoryAboveTheContentRoot()
    {
        WritePackage("demo-skill.zip", Package(ManifestV1));

        await NewSeeder(configuredDirectory: null).SeedAsync();

        await using var context = NewContext();
        Assert.That(await context.Skills.Select(s => s.Name).ToListAsync(), Is.EqualTo(new[] { "demo-skill" }));
    }

    /// <summary>A relative configured path is resolved against the content root, never the CWD.</summary>
    [Test]
    public async Task SeedAsync_RelativeConfiguredDirectory_IsResolvedAgainstTheContentRoot()
    {
        WritePackage("demo-skill.zip", Package(ManifestV1));

        await NewSeeder(Path.Combine("..", "seed", "skills")).SeedAsync();

        await using var context = NewContext();
        Assert.That(context.Skills.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task SeedAsync_EmptySeedDirectory_IsANoOp()
    {
        Assert.DoesNotThrowAsync(async () => await NewSeeder(_seedDirectory).SeedAsync());

        await using var context = NewContext();
        Assert.That(context.Skills.Count(), Is.Zero);
    }

    // ---------------------------------------------------------------- scoping

    /// <summary>
    /// End to end through <see cref="BackgroundService"/>, against a provider with scope validation
    /// on. <see cref="SkillRegistry"/> is registered <c>AddScoped</c>, so a hosted service that
    /// resolved it from the root provider fails here — the alternative being a captured DbContext
    /// that misbehaves much later and much less obviously.
    /// </summary>
    [Test]
    public async Task ExecuteAsync_ResolvesTheScopedRegistryFromItsOwnScope()
    {
        WritePackage("demo-skill.zip", Package(ManifestV1));

        var seeder = NewSeeder(_seedDirectory);

        await RunAsHostedServiceAsync(seeder);

        await using var context = NewContext();
        Assert.That(context.Skills.Count(), Is.EqualTo(1));
    }
}
