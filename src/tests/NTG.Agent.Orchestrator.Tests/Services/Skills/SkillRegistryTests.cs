using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Models.Skills;
using NTG.Agent.Orchestrator.Services.Skills;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services.Skills;

/// <summary>
/// Persistence behaviour for <see cref="SkillRegistry"/>, concentrating on re-import.
/// </summary>
/// <remarks>
/// Re-import is where the damage would be silent. Replacing a skill by deleting and re-inserting
/// works perfectly in every single-skill test while quietly unbinding the skill from every agent
/// using it, and nothing surfaces until someone runs a demo and the agent has no skill. The tests
/// below assert on identity and bindings surviving, not merely on the new content being present.
/// </remarks>
[TestFixture]
public class SkillRegistryTests
{
    private AgentDbContext _context = null!;
    private SkillRegistry _registry = null!;
    private Guid _userId;

    private const string ManifestV1 = """
        ---
        name: demo-skill
        description: First version of the demonstration skill.
        metadata:
          version: "1.0"
        ---

        # Demo v1
        """;

    private const string ManifestV2 = """
        ---
        name: demo-skill
        description: Second version of the demonstration skill.
        metadata:
          version: "2.0"
        ---

        # Demo v2
        """;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AgentDbContext(options);
        _registry = new SkillRegistry(_context, new SkillPackageImporter(), NullLogger<SkillRegistry>.Instance);

        _userId = Guid.NewGuid();
        _context.Users.Add(new User { Id = _userId, UserName = "admin", Email = "admin@test.com" });
        _context.SaveChanges();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private static byte[] Package(string manifest, params (string Path, string Content)[] assets)
    {
        var builder = new TestZipBuilder().AddFile("demo-skill/SKILL.md", manifest);

        foreach (var (path, content) in assets)
        {
            builder.AddFile($"demo-skill/{path}", content);
        }

        return builder.Build();
    }

    // ---------------------------------------------------------------- import

    [Test]
    public async Task ImportAsync_ValidPackage_StoresSkillAndAssets()
    {
        var outcome = await _registry.ImportAsync(
            Package(ManifestV1, ("references/guide.md", "guide body")), "demo-skill.zip", _userId);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True, string.Join(" | ", outcome.Errors));
            Assert.That(outcome.Replaced, Is.False);
            Assert.That(outcome.Name, Is.EqualTo("demo-skill"));
        });

        var stored = await _context.Skills.Include(s => s.Assets).SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.Description, Does.StartWith("First version"));
            Assert.That(stored.Version, Is.EqualTo("1.0"));
            Assert.That(stored.ImportedByUserId, Is.EqualTo(_userId));
            Assert.That(stored.Assets.Select(a => a.RelativePath), Is.EqualTo(new[] { "references/guide.md" }));
        });
    }

    /// <summary>A rejected package must leave the database exactly as it found it.</summary>
    [Test]
    public async Task ImportAsync_RejectedPackage_PersistsNothing()
    {
        var outcome = await _registry.ImportAsync(
            new TestZipBuilder().AddFile("demo-skill/notes.md", "no manifest").Build(),
            "broken.zip",
            _userId);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.Errors, Is.Not.Empty);
            Assert.That(_context.Skills.Count(), Is.Zero);
            Assert.That(_context.SkillAssets.Count(), Is.Zero);
        });
    }

    // ---------------------------------------------------------------- re-import

    [Test]
    public async Task ImportAsync_SameName_UpdatesInPlaceRatherThanAddingASecondRow()
    {
        await _registry.ImportAsync(Package(ManifestV1), "demo-skill.zip", _userId);
        var outcome = await _registry.ImportAsync(Package(ManifestV2), "demo-skill.zip", _userId);

        var stored = await _context.Skills.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Replaced, Is.True);
            Assert.That(stored.Description, Does.StartWith("Second version"));
            Assert.That(stored.Version, Is.EqualTo("2.0"));
            Assert.That(stored.Body, Does.Contain("Demo v2"));
        });
    }

    /// <summary>
    /// The identity check. AgentSkill rows key off Skill.Id, so a re-import that mints a new id
    /// unbinds the skill from every agent that was using it.
    /// </summary>
    [Test]
    public async Task ImportAsync_SameName_PreservesTheSkillId()
    {
        var first = await _registry.ImportAsync(Package(ManifestV1), "demo-skill.zip", _userId);
        var second = await _registry.ImportAsync(Package(ManifestV2), "demo-skill.zip", _userId);

        Assert.That(second.SkillId, Is.EqualTo(first.SkillId));
    }

    /// <summary>The consequence of the previous test, asserted end to end.</summary>
    [Test]
    public async Task ImportAsync_SameName_KeepsExistingAgentBindings()
    {
        var first = await _registry.ImportAsync(Package(ManifestV1), "demo-skill.zip", _userId);

        var agentId = Guid.NewGuid();
        _context.Agents.Add(new AgentModel { Id = agentId, Name = "Test Agent", OwnerUserId = _userId });
        _context.AgentSkills.Add(new AgentSkill
        {
            AgentId = agentId,
            SkillId = first.SkillId!.Value,
            IsEnabled = true,
        });
        await _context.SaveChangesAsync();

        await _registry.ImportAsync(Package(ManifestV2), "demo-skill.zip", _userId);

        var binding = await _context.AgentSkills.SingleOrDefaultAsync(b => b.AgentId == agentId);

        Assert.Multiple(() =>
        {
            Assert.That(binding, Is.Not.Null, "re-import must not unbind the skill from its agents");
            Assert.That(binding!.SkillId, Is.EqualTo(first.SkillId!.Value));
            Assert.That(binding.IsEnabled, Is.True);
        });
    }

    [Test]
    public async Task ImportAsync_SameName_ReplacesAssetsRatherThanAccumulatingThem()
    {
        await _registry.ImportAsync(
            Package(ManifestV1, ("references/old.md", "old"), ("assets/gone.txt", "gone")),
            "demo-skill.zip",
            _userId);

        await _registry.ImportAsync(
            Package(ManifestV2, ("references/new.md", "new")), "demo-skill.zip", _userId);

        var assets = await _context.SkillAssets.Select(a => a.RelativePath).ToListAsync();

        Assert.That(assets, Is.EqualTo(new[] { "references/new.md" }));
    }

    // ---------------------------------------------------------------- read

    [Test]
    public async Task ListAsync_ProjectsEmailAndAssetCount()
    {
        await _registry.ImportAsync(
            Package(ManifestV1, ("references/a.md", "a"), ("references/b.md", "b")),
            "demo-skill.zip",
            _userId);

        var list = await _registry.ListAsync();

        Assert.That(list, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(list[0].Name, Is.EqualTo("demo-skill"));
            Assert.That(list[0].ImportedByEmail, Is.EqualTo("admin@test.com"));
            Assert.That(list[0].AssetCount, Is.EqualTo(2));
        });
    }

    /// <summary>An unknown importer must not blank the whole row or throw.</summary>
    [Test]
    public async Task ListAsync_UnknownImporter_YieldsEmptyEmail()
    {
        await _registry.ImportAsync(Package(ManifestV1), "demo-skill.zip", Guid.NewGuid());

        var list = await _registry.ListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].ImportedByEmail, Is.Empty);
            Assert.That(list[0].Name, Is.EqualTo("demo-skill"));
        });
    }

    [Test]
    public async Task GetAsync_ReturnsAssetPathsAndSizes()
    {
        var outcome = await _registry.ImportAsync(
            Package(ManifestV1, ("references/guide.md", "twelve bytes")), "demo-skill.zip", _userId);

        var detail = await _registry.GetAsync(outcome.SkillId!.Value);

        Assert.That(detail, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(detail!.Body, Does.Contain("Demo v1"));
            Assert.That(detail.ImportedByEmail, Is.EqualTo("admin@test.com"));
            Assert.That(detail.Assets, Has.Count.EqualTo(1));
            Assert.That(detail.Assets[0].RelativePath, Is.EqualTo("references/guide.md"));
            Assert.That(detail.Assets[0].SizeBytes, Is.EqualTo(12));
        });
    }

    [Test]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        Assert.That(await _registry.GetAsync(Guid.NewGuid()), Is.Null);
    }

    // ---------------------------------------------------------------- delete

    [Test]
    public async Task DeleteAsync_RemovesSkillAssetsAndBindings()
    {
        var outcome = await _registry.ImportAsync(
            Package(ManifestV1, ("references/guide.md", "guide")), "demo-skill.zip", _userId);

        var agentId = Guid.NewGuid();
        _context.Agents.Add(new AgentModel { Id = agentId, Name = "Test Agent", OwnerUserId = _userId });
        _context.AgentSkills.Add(new AgentSkill
        {
            AgentId = agentId,
            SkillId = outcome.SkillId!.Value,
            IsEnabled = true,
        });
        await _context.SaveChangesAsync();

        var deleted = await _registry.DeleteAsync(outcome.SkillId!.Value);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(_context.Skills.Count(), Is.Zero);
            Assert.That(_context.SkillAssets.Count(), Is.Zero);
            Assert.That(_context.AgentSkills.Count(), Is.Zero, "orphaned bindings would break the agent's tool list");
        });
    }

    [Test]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        Assert.That(await _registry.DeleteAsync(Guid.NewGuid()), Is.False);
    }
}
