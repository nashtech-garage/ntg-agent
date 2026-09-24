using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Models.Skills;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Services.Skills;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services.Skills;

/// <summary>
/// <see cref="SkillActivityLog"/> and its two writers: <c>load_skill</c> (via
/// <see cref="SkillTools.CreateLoadSkill"/>) and <c>render_skill_surface</c> (via
/// <see cref="SkillTools.CreateRenderSurface"/>). This is the narration behind the "Thought for N
/// seconds" panel's skill line, so what matters most is not that a success narrates — that part is
/// easy — but that a refusal narrates nothing. A refused load or render named a skill the model
/// never actually got to use, and telling the user otherwise would misrepresent what the agent did.
/// </summary>
[TestFixture]
public class SkillActivityLogTests
{
    private const string PanelSurface = """
        {
          "surfaceId": "srf-panel",
          "components": [
            { "id": "root", "component": "Column", "children": ["title"] },
            { "id": "title", "component": "Text", "text": "Panel" }
          ],
          "data": {}
        }
        """;

    private AgentDbContext _context = null!;
    private SkillRegistry _registry = null!;
    private SkillActivityLog _activityLog = null!;
    private RenderableToolCapture _capture = null!;
    private Guid _userId;
    private Guid _agentId;

    [SetUp]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AgentDbContext(options);
        _registry = new SkillRegistry(_context, new SkillPackageImporter(), NullLogger<SkillRegistry>.Instance);
        _activityLog = new SkillActivityLog();
        _capture = new RenderableToolCapture();

        _userId = Guid.NewGuid();
        _agentId = Guid.NewGuid();

        _context.Users.Add(new User { Id = _userId, UserName = "admin", Email = "admin@test.com" });
        _context.Agents.Add(new AgentModel { Id = _agentId, Name = "Agent", OwnerUserId = _userId });
        await _context.SaveChangesAsync();

        // Bound-and-enabled, and bound-but-disabled: the same pair of shapes SurfaceRenderFunction's
        // own tests use, because "disabled" and "never bound" are the two ways a load can be refused
        // and both have to leave the log untouched.
        var demo = await ImportAsync("demo-skill", ("assets/panel.json", PanelSurface));
        var disabled = await ImportAsync("disabled-skill", ("assets/panel.json", PanelSurface));

        Bind(demo, isEnabled: true);
        Bind(disabled, isEnabled: false);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private static string Manifest(string name) => $"""
        ---
        name: {name}
        description: A demonstration skill used by the skill activity log tests.
        metadata:
          version: "1.0"
        ---

        # {name}

        Body text.
        """;

    private async Task<Guid> ImportAsync(string name, params (string Path, string Content)[] assets)
    {
        var builder = new TestZipBuilder().AddFile($"{name}/SKILL.md", Manifest(name));

        foreach (var (path, content) in assets)
        {
            builder.AddFile($"{name}/{path}", content);
        }

        var outcome = await _registry.ImportAsync(builder.Build(), $"{name}.zip", _userId);

        Assert.That(outcome.Succeeded, Is.True, string.Join(" | ", outcome.Errors));

        return outcome.SkillId!.Value;
    }

    private void Bind(Guid skillId, bool isEnabled)
    {
        _context.AgentSkills.Add(new AgentSkill
        {
            AgentId = _agentId,
            SkillId = skillId,
            IsEnabled = isEnabled,
        });
        _context.SaveChanges();
    }

    // ---------------------------------------------------------------- load_skill

    [Test]
    public async Task LoadSkill_SuccessfulLoad_Narrates()
    {
        var loadSkill = SkillTools.CreateLoadSkill(_registry, _activityLog, _agentId, NullLogger.Instance);

        await loadSkill.InvokeAsync(new AIFunctionArguments { ["skill"] = "demo-skill" });

        var lines = _activityLog.DrainPending().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(1), "one successful load must narrate exactly once");
            Assert.That(lines[0], Does.Contain("demo-skill"), "the narration must name the skill that was used");
        });
    }

    /// <summary>
    /// Covers both ways a load is refused — a disabled binding and a name with no binding at all —
    /// so the assertion cannot pass merely because <c>GetActiveSkillBodyAsync</c> happened to return
    /// null for one particular reason.
    /// </summary>
    [TestCase("disabled-skill")]
    [TestCase("no-such-skill")]
    public async Task LoadSkill_RefusedLoad_DoesNotNarrate(string skillName)
    {
        var loadSkill = SkillTools.CreateLoadSkill(_registry, _activityLog, _agentId, NullLogger.Instance);

        var result = await loadSkill.InvokeAsync(new AIFunctionArguments { ["skill"] = skillName });

        Assert.Multiple(() =>
        {
            Assert.That(result?.ToString(), Does.Contain("No skill named"), "sanity: the load must actually be refused");
            Assert.That(_activityLog.DrainPending(), Is.Empty, "a refused load must not claim the skill was used");
        });
    }

    // ---------------------------------------------------------------- render_skill_surface

    [Test]
    public async Task RenderSurface_SuccessfulRender_Narrates()
    {
        var render = SkillTools.CreateRenderSurface(_registry, _capture, _activityLog, _agentId, NullLogger.Instance);

        await render.InvokeAsync(new AIFunctionArguments
        {
            ["skill"] = "demo-skill",
            ["surface"] = "panel",
        });

        var lines = _activityLog.DrainPending().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(1), "one successful render must narrate exactly once");
            Assert.That(lines[0], Does.Contain("panel"), "the narration must name the surface that was rendered");
        });
    }

    /// <summary>
    /// A render can fail after the skill/surface lookup succeeds — here on an unknown surface name —
    /// and the log must stay empty in that case too, not just when the lookup itself fails.
    /// </summary>
    [Test]
    public async Task RenderSurface_RefusedRender_DoesNotNarrate()
    {
        var render = SkillTools.CreateRenderSurface(_registry, _capture, _activityLog, _agentId, NullLogger.Instance);

        var result = await render.InvokeAsync(new AIFunctionArguments
        {
            ["skill"] = "demo-skill",
            ["surface"] = "no-such-surface",
        });

        Assert.Multiple(() =>
        {
            Assert.That(result?.ToString(), Does.Contain("No surface named"), "sanity: the render must actually be refused");
            Assert.That(_activityLog.DrainPending(), Is.Empty, "a refused render must not claim a surface was drawn");
        });
    }

    // ---------------------------------------------------------------- SkillActivityLog itself

    /// <summary>
    /// Draining is destructive by design — <see cref="AgentService"/> drains once per streamed
    /// update, so a line left behind after a drain would be narrated a second time on the next one.
    /// </summary>
    [Test]
    public void DrainPending_AfterDraining_IsEmpty()
    {
        var log = new SkillActivityLog();
        log.Add("Using the demo-skill skill.");

        _ = log.DrainPending().ToList();

        Assert.That(log.DrainPending(), Is.Empty);
    }

    [Test]
    public void DrainPending_OnAFreshLog_IsEmpty()
    {
        Assert.That(new SkillActivityLog().DrainPending(), Is.Empty);
    }
}
