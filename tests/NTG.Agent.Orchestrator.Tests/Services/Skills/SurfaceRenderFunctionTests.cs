using System.Text.Json.Nodes;
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
/// The <c>render_skill_surface</c> tool, built through <see cref="SkillTools.CreateRenderSurface"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every test goes through a real import into a real <see cref="AgentDbContext"/> and a real
/// <see cref="AgentSkill"/> binding, because the two things worth asserting about this class both
/// live in that data. The first is the split audience: the browser gets the full A2UI operations
/// through <see cref="RenderableToolCapture"/> and the model gets a one-line receipt, so a
/// regression that returns the payload to the model would still render correctly and would only
/// show up as context bloat nobody attributes to this class.
/// </para>
/// <para>
/// The second is scoping. The agent id is closed over at construction and never taken as an
/// argument, so the model's only lever is the skill name. Naming a skill bound to a different
/// agent, or bound-but-disabled, must yield nothing — asserted on the capture being empty rather
/// than only on the returned text, since a capture written before the failure would reach the
/// browser regardless of what the model was told.
/// </para>
/// </remarks>
[TestFixture]
public class SurfaceRenderFunctionTests
{
    private const string CatalogId = "https://a2ui.org/specification/v0_9/basic_catalog.json";
    private const string ProtocolVersion = "v0.9";

    /// <summary>Deliberately not named after its file: the receipt must not leak the surfaceId.</summary>
    private const string PanelSurfaceId = "srf-panel-7c1d";

    private static readonly string[] OperationKinds = ["createSurface", "updateComponents", "updateDataModel"];

    private static readonly string[] TravelSurfaces = ["trip-search", "trip-results", "trip-confirm"];

    private const string PanelSurface = """
        {
          "surfaceId": "srf-panel-7c1d",
          "components": [
            { "id": "root", "component": "Column", "children": ["title", "who", "city"] },
            { "id": "title", "component": "Text", "text": "Panel" },
            { "id": "who", "component": "TextField", "label": "Name", "value": { "path": "/form/name" } },
            { "id": "city", "component": "TextField", "label": "City", "value": { "path": "/form/address/city" } }
          ],
          "data": {
            "form": {
              "name": "default-name",
              "address": { "city": "default-city", "country": "default-country" },
              "note": "untouched"
            }
          }
        }
        """;

    private AgentDbContext _context = null!;
    private SkillRegistry _registry = null!;
    private RenderableToolCapture _capture = null!;
    private AIFunction _renderer = null!;
    private Guid _userId;
    private Guid _agentA;
    private Guid _agentB;

    [SetUp]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AgentDbContext(options);
        _registry = new SkillRegistry(_context, new SkillPackageImporter(), NullLogger<SkillRegistry>.Instance);
        _capture = new RenderableToolCapture();

        _userId = Guid.NewGuid();
        _agentA = Guid.NewGuid();
        _agentB = Guid.NewGuid();

        _context.Users.Add(new User { Id = _userId, UserName = "admin", Email = "admin@test.com" });
        _context.Agents.Add(new AgentModel { Id = _agentA, Name = "Agent A", OwnerUserId = _userId });
        _context.Agents.Add(new AgentModel { Id = _agentB, Name = "Agent B", OwnerUserId = _userId });
        await _context.SaveChangesAsync();

        // Three skills carrying the same surface: one usable, one bound-but-disabled, one bound to
        // nobody. Identical assets are the point — a refusal must come from the binding check and
        // not from the surface happening to be missing.
        var demo = await ImportAsync("demo-skill", ("assets/panel.json", PanelSurface));
        var disabled = await ImportAsync("disabled-skill", ("assets/panel.json", PanelSurface));
        await ImportAsync("unbound-skill", ("assets/panel.json", PanelSurface));

        Bind(_agentA, demo, isEnabled: true);
        Bind(_agentA, disabled, isEnabled: false);

        _renderer = SkillTools.CreateRenderSurface(_registry, _capture, _agentA, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    // ---------------------------------------------------------------- fixtures

    private static string Manifest(string name) => $"""
        ---
        name: {name}
        description: A demonstration skill used by the surface render tests.
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

    private void Bind(Guid agentId, Guid skillId, bool isEnabled)
    {
        _context.AgentSkills.Add(new AgentSkill
        {
            AgentId = agentId,
            SkillId = skillId,
            IsEnabled = isEnabled,
        });
        _context.SaveChanges();
    }

    private Task<string> RenderAsync(string skill, string surface, object? values = null) =>
        RenderAsync(_renderer, skill, surface, values);

    private static async Task<string> RenderAsync(
        AIFunction function, string skill, string surface, object? values = null)
    {
        var arguments = new AIFunctionArguments
        {
            ["skill"] = skill,
            ["surface"] = surface,
        };

        if (values is not null)
        {
            arguments["values"] = values;
        }

        var result = await function.InvokeAsync(arguments);

        return result?.ToString() ?? string.Empty;
    }

    private List<CapturedToolCall> Captured() => [.. _capture.DrainPending()];

    private static JsonArray Operations(CapturedToolCall call)
    {
        var payload = JsonNode.Parse(call.Result) as JsonObject;
        Assert.That(payload, Is.Not.Null, "the captured result must be a JSON object");

        var operations = payload!["a2ui_operations"] as JsonArray;
        Assert.That(operations, Is.Not.Null, "the browser renders whatever is under 'a2ui_operations'");

        return operations!;
    }

    private static JsonObject Operation(JsonArray operations, string kind) =>
        (JsonObject)operations.Single(o => o?[kind] is not null)![kind]!;

    private static JsonObject DataModel(JsonArray operations) =>
        (JsonObject)Operation(operations, "updateDataModel")["value"]!;

    private async Task<JsonArray> RenderAndCaptureAsync(string skill, string surface, object? values = null)
    {
        _ = await RenderAsync(skill, surface, values);

        var captured = Captured();
        Assert.That(captured, Has.Count.EqualTo(1), "one render must produce one captured call");

        return Operations(captured[0]);
    }

    // ---------------------------------------------------------------- happy path

    [Test]
    public async Task Render_ValidSurface_CapturesExactlyOneCall()
    {
        await RenderAsync("demo-skill", "panel");

        var captured = Captured();

        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(captured[0].Name, Is.EqualTo("render_skill_surface"));
            Assert.That(captured[0].Arguments["skill"], Is.EqualTo("demo-skill"));
            Assert.That(captured[0].Arguments["surface"], Is.EqualTo("panel"));
            Assert.That(captured[0].CallId, Is.Not.Empty);
        });
    }

    /// <summary>
    /// The wire contract, checked against <c>@a2ui/web_core/src/v0_9/schemas/server_to_client.json</c>:
    /// each message is one of createSurface / updateComponents / updateDataModel, carries
    /// <c>version: "v0.9"</c>, and names the same surfaceId. Order matters — the schema states that
    /// createSurface MUST precede the messages that populate the surface.
    /// </summary>
    [Test]
    public async Task Render_ValidSurface_EmitsCreateThenComponentsThenDataModel()
    {
        var operations = await RenderAndCaptureAsync("demo-skill", "panel");

        Assert.That(operations, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            for (var i = 0; i < OperationKinds.Length; i++)
            {
                var operation = (JsonObject)operations[i]!;

                Assert.That(
                    operation.ContainsKey(OperationKinds[i]),
                    Is.True,
                    $"operation {i} must be a {OperationKinds[i]} message");
                Assert.That(
                    operation["version"]!.GetValue<string>(),
                    Is.EqualTo(ProtocolVersion),
                    "the schema pins version to the const \"v0.9\"");
                Assert.That(
                    operation[OperationKinds[i]]!["surfaceId"]!.GetValue<string>(),
                    Is.EqualTo(PanelSurfaceId),
                    "all three messages must address the surfaceId taken from the template");
            }
        });
    }

    /// <summary>
    /// createSurface requires a catalogId, and it has to be the catalog the templates were
    /// validated against at import — a mismatch renders an empty card with no error anywhere.
    /// </summary>
    [Test]
    public async Task Render_CreateSurface_CarriesTheV09BasicCatalogId()
    {
        var operations = await RenderAndCaptureAsync("demo-skill", "panel");

        Assert.That(
            Operation(operations, "createSurface")["catalogId"]!.GetValue<string>(),
            Is.EqualTo(CatalogId));
    }

    [Test]
    public async Task Render_UpdateComponents_CarriesTheTemplatesComponentTree()
    {
        var operations = await RenderAndCaptureAsync("demo-skill", "panel");

        var components = (JsonArray)Operation(operations, "updateComponents")["components"]!;

        Assert.Multiple(() =>
        {
            Assert.That(components, Has.Count.EqualTo(4));
            Assert.That(
                components.Select(c => c!["id"]!.GetValue<string>()),
                Does.Contain("root"),
                "updateComponents is rejected without a component whose id is 'root'");
        });
    }

    /// <summary>
    /// updateDataModel takes <c>path</c> and <c>value</c>. <c>contents</c> is the v0.8 spelling and
    /// is silently ignored by the v0.9 client, which renders the surface with an empty data model —
    /// every binding blank, every input frozen, and no error raised.
    /// </summary>
    [Test]
    public async Task Render_UpdateDataModel_UsesPathAndValueRatherThanContents()
    {
        var operations = await RenderAndCaptureAsync("demo-skill", "panel");

        var updateDataModel = Operation(operations, "updateDataModel");

        Assert.Multiple(() =>
        {
            Assert.That(updateDataModel["path"]!.GetValue<string>(), Is.EqualTo("/"));
            Assert.That(updateDataModel["value"], Is.Not.Null);
            Assert.That(updateDataModel.ContainsKey("contents"), Is.False);
        });
    }

    /// <summary>
    /// The entire reason this class is not built on <c>CapturingAIFunction</c>. The operations are
    /// for the browser; the model gets a receipt. Returning the payload instead would put hundreds
    /// of lines of component JSON into context on every render and still look correct on screen.
    /// </summary>
    [Test]
    public async Task Render_ReturnsAReceiptWithoutTheSurfacePayload()
    {
        var result = await RenderAsync("demo-skill", "panel");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("panel"), "the model should know which surface it drew");
            Assert.That(result, Does.Not.Contain("components"));
            Assert.That(result, Does.Not.Contain(PanelSurfaceId));
            Assert.That(result, Does.Not.Contain("a2ui_operations"));
            Assert.That(result, Has.Length.LessThan(300), "a receipt, not a payload");
            Assert.That(Captured(), Has.Count.EqualTo(1), "the operations still have to reach the browser");
        });
    }

    // ---------------------------------------------------------------- values merging

    /// <summary>
    /// The model fills in a template it did not author, so its values have to land at the nested
    /// paths the components bind to rather than replacing the data model wholesale.
    /// </summary>
    [Test]
    public async Task Render_Values_OverrideTemplateDefaultsAtNestedPaths()
    {
        var operations = await RenderAndCaptureAsync(
            "demo-skill",
            "panel",
            JsonNode.Parse("""{ "form": { "name": "Ada", "address": { "city": "Hanoi" } } }"""));

        var data = DataModel(operations);

        Assert.Multiple(() =>
        {
            Assert.That(data["form"]!["name"]!.GetValue<string>(), Is.EqualTo("Ada"));
            Assert.That(data["form"]!["address"]!["city"]!.GetValue<string>(), Is.EqualTo("Hanoi"));
        });
    }

    /// <summary>
    /// A shallow assignment at <c>/form</c> or <c>/form/address</c> would drop every sibling key the
    /// model did not mention, and the components bound to them would render blank.
    /// </summary>
    [Test]
    public async Task Render_Values_PreserveTemplateKeysTheModelDidNotMention()
    {
        var operations = await RenderAndCaptureAsync(
            "demo-skill",
            "panel",
            JsonNode.Parse("""{ "form": { "address": { "city": "Hanoi" } } }"""));

        var data = DataModel(operations);

        Assert.Multiple(() =>
        {
            Assert.That(data["form"]!["name"]!.GetValue<string>(), Is.EqualTo("default-name"));
            Assert.That(data["form"]!["address"]!["country"]!.GetValue<string>(), Is.EqualTo("default-country"));
            Assert.That(data["form"]!["note"]!.GetValue<string>(), Is.EqualTo("untouched"));
        });
    }

    [Test]
    public async Task Render_WithoutValues_RendersTheTemplateDefaults()
    {
        var result = await RenderAsync("demo-skill", "panel");

        var captured = Captured();
        Assert.That(captured, Has.Count.EqualTo(1));

        var data = DataModel(Operations(captured[0]));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Rendered"));
            Assert.That(data["form"]!["name"]!.GetValue<string>(), Is.EqualTo("default-name"));
            Assert.That(data["form"]!["address"]!["city"]!.GetValue<string>(), Is.EqualTo("default-city"));
        });
    }

    /// <summary>
    /// <c>values</c> is model-authored and typed only by a JSON schema nothing enforces, so a
    /// scalar, a list or a fragment of prose can all arrive here. None of them may throw: an
    /// exception aborts the run, where ignoring the garbage still renders a usable surface.
    /// </summary>
    [TestCase("not json at all")]
    [TestCase("[1, 2, 3]")]
    [TestCase("\"just a string\"")]
    [TestCase(42)]
    [TestCase(true)]
    public async Task Render_GarbageValues_IsIgnoredRatherThanThrowing(object values)
    {
        var operations = await RenderAndCaptureAsync("demo-skill", "panel", values);

        Assert.That(
            DataModel(operations)["form"]!["name"]!.GetValue<string>(),
            Is.EqualTo("default-name"),
            "unusable values must leave the template's defaults in place");
    }

    /// <summary>Providers hand structured arguments back as a JSON string often enough to matter.</summary>
    [Test]
    public async Task Render_ValuesAsJsonString_IsParsedRatherThanIgnored()
    {
        var operations = await RenderAndCaptureAsync("demo-skill", "panel", """{ "form": { "name": "Ada" } }""");

        Assert.That(DataModel(operations)["form"]!["name"]!.GetValue<string>(), Is.EqualTo("Ada"));
    }

    // ---------------------------------------------------------------- surface naming

    /// <summary>
    /// Models pass all three forms regardless of what the tool schema says. Rejecting two of them
    /// buys a retry round-trip and, often enough, an apology to the user instead of a surface.
    /// </summary>
    [TestCase("panel")]
    [TestCase("panel.json")]
    [TestCase("assets/panel.json")]
    [TestCase("  panel  ")]
    [TestCase("assets/panel")]
    public async Task Render_SurfaceNameForms_AllResolveToTheSameSurface(string surface)
    {
        var operations = await RenderAndCaptureAsync("demo-skill", surface);

        Assert.That(
            Operation(operations, "createSurface")["surfaceId"]!.GetValue<string>(),
            Is.EqualTo(PanelSurfaceId));
    }

    // ---------------------------------------------------------------- failure paths

    private async Task AssertRefusedAsync(string skill, string surface, AIFunction? function = null)
    {
        var result = await RenderAsync(function ?? _renderer, skill, surface);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Empty, "the model needs a message it can recover from");
            Assert.That(result, Does.Not.Contain("a2ui_operations"));
            Assert.That(result, Does.Not.Contain("components"));
            Assert.That(Captured(), Is.Empty, "a refused render must put nothing in front of the user");
        });
    }

    [Test]
    public async Task Render_UnknownSurface_ReturnsAMessageAndCapturesNothing()
    {
        await AssertRefusedAsync("demo-skill", "no-such-surface");
    }

    [Test]
    public async Task Render_UnknownSkill_ReturnsAMessageAndCapturesNothing()
    {
        await AssertRefusedAsync("no-such-skill", "panel");
    }

    /// <summary>
    /// The skill and the surface both exist; only the binding is missing. A lookup that resolved
    /// the skill by name alone would pass this test's happy-path twin and fail here.
    /// </summary>
    [Test]
    public async Task Render_SkillNotBoundToThisAgent_ReturnsAMessageAndCapturesNothing()
    {
        await AssertRefusedAsync("unbound-skill", "panel");
    }

    /// <summary>
    /// Disabling a binding is the admin's off switch. It has to hold at render time, not only in
    /// the catalog — the model can name a skill it saw in an earlier turn.
    /// </summary>
    [Test]
    public async Task Render_DisabledBinding_ReturnsAMessageAndCapturesNothing()
    {
        await AssertRefusedAsync("disabled-skill", "panel");
    }

    [Test]
    public async Task Render_MissingSkillArgument_ReturnsAMessageAndCapturesNothing()
    {
        var missing = await _renderer.InvokeAsync(new AIFunctionArguments { ["surface"] = "panel" });

        Assert.Multiple(() =>
        {
            Assert.That(missing?.ToString(), Does.Contain("required"));
            Assert.That(Captured(), Is.Empty);
        });
    }

    [Test]
    public async Task Render_MissingSurfaceArgument_ReturnsAMessageAndCapturesNothing()
    {
        var missing = await _renderer.InvokeAsync(new AIFunctionArguments { ["skill"] = "demo-skill" });

        Assert.Multiple(() =>
        {
            Assert.That(missing?.ToString(), Does.Contain("required"));
            Assert.That(Captured(), Is.Empty);
        });
    }

    [Test]
    public async Task Render_NoArgumentsAtAll_ReturnsAMessageAndCapturesNothing()
    {
        var missing = await _renderer.InvokeAsync(new AIFunctionArguments());

        Assert.Multiple(() =>
        {
            Assert.That(missing?.ToString(), Does.Contain("required"));
            Assert.That(Captured(), Is.Empty);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Render_BlankArguments_ReturnAMessageAndCaptureNothing(string blank)
    {
        var bothBlank = await RenderAsync(_renderer, blank, blank);
        var surfaceBlank = await RenderAsync(_renderer, "demo-skill", blank);

        Assert.Multiple(() =>
        {
            Assert.That(bothBlank, Does.Contain("required"));
            Assert.That(surfaceBlank, Does.Contain("required"));
            Assert.That(Captured(), Is.Empty);
        });
    }

    // ---------------------------------------------------------------- scoping

    /// <summary>
    /// The case the design exists for. Agent B's tool is handed the name of a skill bound only to
    /// agent A — a name the model could have learned from any earlier conversation, or simply
    /// guessed. The agent id is not a parameter, so there is nothing for the model to override, and
    /// the render must fail with the browser seeing nothing.
    /// </summary>
    [Test]
    public async Task Render_SkillBoundToAnotherAgent_IsRefusedAndCapturesNothing()
    {
        var otherAgentsRenderer = SkillTools.CreateRenderSurface(
            _registry, _capture, _agentB, NullLogger.Instance);

        await AssertRefusedAsync("demo-skill", "panel", otherAgentsRenderer);
    }

    /// <summary>
    /// The same skill through the agent it is actually bound to. Without this the test above would
    /// pass just as happily if rendering were broken for every agent.
    /// </summary>
    [Test]
    public async Task Render_SameSkillThroughItsOwnAgent_Succeeds()
    {
        await RenderAsync("demo-skill", "panel");

        Assert.That(Captured(), Has.Count.EqualTo(1));
    }

    /// <summary>
    /// The surface name is concatenated into <c>assets/{name}.json</c> and used as a lookup key, so
    /// a traversal cannot leave the database — but the name is model-controlled and the key is a
    /// path, which is exactly the shape that becomes a file read the day someone caches assets to
    /// disk. Asserted now so that change cannot land quietly.
    /// </summary>
    [TestCase("../SKILL")]
    [TestCase("../../etc/passwd")]
    [TestCase("assets/../SKILL")]
    [TestCase("../unbound-skill/assets/panel")]
    [TestCase("..%2fSKILL")]
    [TestCase("/etc/passwd")]
    public async Task Render_TraversalSurfaceName_ReturnsNothingAndCapturesNothing(string surface)
    {
        await AssertRefusedAsync("demo-skill", surface);
    }

    /// <summary>A traversal in the skill name has no more effect than any other unknown name.</summary>
    [TestCase("../demo-skill")]
    [TestCase("demo-skill/../unbound-skill")]
    public async Task Render_TraversalSkillName_ReturnsNothingAndCapturesNothing(string skill)
    {
        await AssertRefusedAsync(skill, "panel");
    }

    // ---------------------------------------------------------------- seeded package

    /// <summary>
    /// The package actually shipped in <c>seed/skills/</c>, rendered through the same path a demo
    /// takes. The synthetic fixture above proves the mechanics; this proves the templates people
    /// will actually see still render.
    /// </summary>
    [TestCase("trip-search")]
    [TestCase("trip-results")]
    [TestCase("trip-confirm")]
    public async Task Render_SeededTravelPlanningSurface_Renders(string surface)
    {
        if (!await TryImportTravelPlanningAsync())
        {
            return;
        }

        await RenderAsync("travel-planning", surface);

        var captured = Captured();
        Assert.That(captured, Has.Count.EqualTo(1));

        var operations = Operations(captured[0]);

        Assert.That(operations, Has.Count.EqualTo(3));
        Assert.That(
            Operation(operations, "createSurface")["surfaceId"]!.GetValue<string>(),
            Is.EqualTo(surface));
    }

    [Test]
    public async Task Render_SeededTripSearch_MergesValuesOverTheShippedDefaults()
    {
        if (!await TryImportTravelPlanningAsync())
        {
            return;
        }

        await RenderAsync(
            "travel-planning",
            "trip-search",
            JsonNode.Parse("""{ "trip": { "destination": "Kyoto" } }"""));

        var captured = Captured();
        Assert.That(captured, Has.Count.EqualTo(1));

        var trip = DataModel(Operations(captured[0]))["trip"]!;

        Assert.Multiple(() =>
        {
            Assert.That(trip["destination"]!.GetValue<string>(), Is.EqualTo("Kyoto"));
            Assert.That(trip["travellers"]!.GetValue<int>(), Is.EqualTo(2), "an untouched default must survive");
            Assert.That(((JsonArray)trip["style"]!).Count, Is.EqualTo(1), "the seeded selection must survive");
        });
    }

    [Test]
    public async Task Render_SeededPackage_ExposesOnlyItsThreeSurfaces()
    {
        if (!await TryImportTravelPlanningAsync())
        {
            return;
        }

        foreach (var surface in TravelSurfaces)
        {
            await RenderAsync("travel-planning", surface);
        }

        Assert.That(Captured(), Has.Count.EqualTo(TravelSurfaces.Length));

        await AssertRefusedAsync("travel-planning", "SKILL");
    }

    /// <summary>Imports and binds the seeded package, or ignores the test when it is not present.</summary>
    private async Task<bool> TryImportTravelPlanningAsync()
    {
        var package = FindRepositoryFile(Path.Combine("seed", "skills", "travel-planning.zip"));
        if (package is null)
        {
            Assert.Ignore("seed/skills/travel-planning.zip not found from the test output directory");
            return false;
        }

        var outcome = await _registry.ImportAsync(
            await File.ReadAllBytesAsync(package), "travel-planning.zip", _userId);

        Assert.That(outcome.Succeeded, Is.True, string.Join(" | ", outcome.Errors));

        Bind(_agentA, outcome.SkillId!.Value, isEnabled: true);
        return true;
    }

    private static string? FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
