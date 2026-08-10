using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NTG.Agent.Common.Dtos.Skills;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Services.Skills;
using NTG.Agent.Orchestrator.Tests.Services.Skills;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

/// <summary>
/// Endpoint contract tests for <see cref="SkillsController"/>.
/// </summary>
/// <remarks>
/// These assert the wire contract the Blazor admin client depends on, which is sharper than it
/// looks: the client deserialises a successful import into <c>SkillDetail</c>, and a record with
/// all-default members deserialises from an error payload <em>without throwing</em>. A 200 carrying
/// an error body would therefore render "Imported '' successfully" and close the dialog. The status
/// code is the only thing separating success from failure, so it is asserted explicitly everywhere.
/// </remarks>
[TestFixture]
public class SkillsControllerTests
{
    private AgentDbContext _context = null!;
    private SkillsController _controller = null!;
    private Guid _userId;

    private const string Manifest = """
        ---
        name: demo-skill
        description: A demonstration skill used by the controller tests.
        license: Apache-2.0
        compatibility: Requires nothing in particular.
        metadata:
          version: "1.0"
        ---

        # Demo

        Body text for the demo skill.
        """;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AgentDbContext(options);
        _userId = Guid.NewGuid();
        _context.Users.Add(new User { Id = _userId, UserName = "admin", Email = "admin@test.com" });
        _context.SaveChanges();

        var registry = new SkillRegistry(_context, new SkillPackageImporter(), NullLogger<SkillRegistry>.Instance);

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
        ], "mock"));

        _controller = new SkillsController(registry, NullLogger<SkillsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user },
            },
        };
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private static IFormFile AsFormFile(byte[] bytes, string fileName = "demo-skill.zip") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/zip",
        };

    private static byte[] ValidPackage(params (string Path, string Content)[] assets)
    {
        var builder = new TestZipBuilder().AddFile("demo-skill/SKILL.md", Manifest);

        foreach (var (path, content) in assets)
        {
            builder.AddFile($"demo-skill/{path}", content);
        }

        return builder.Build();
    }

    private async Task<SkillDetail> ImportAsync(params (string Path, string Content)[] assets)
    {
        var result = await _controller.ImportSkill(AsFormFile(ValidPackage(assets)), CancellationToken.None);
        return (SkillDetail)((OkObjectResult)result).Value!;
    }

    // ---------------------------------------------------------------- import

    [Test]
    public async Task ImportSkill_ValidPackage_ReturnsFullDetailNotAStub()
    {
        var result = await _controller.ImportSkill(
            AsFormFile(ValidPackage(("references/guide.md", "guide"))), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var detail = ((OkObjectResult)result).Value as SkillDetail;

        Assert.That(detail, Is.Not.Null);
        Assert.Multiple(() =>
        {
            // Every field the UI renders must be populated — a narrower payload binds to defaults
            // and displays as a blank skill with a confident success message.
            Assert.That(detail!.Name, Is.EqualTo("demo-skill"));
            Assert.That(detail.Description, Does.StartWith("A demonstration skill"));
            Assert.That(detail.Version, Is.EqualTo("1.0"));
            Assert.That(detail.ImportedByEmail, Is.EqualTo("admin@test.com"));
            Assert.That(detail.Body, Does.Contain("# Demo"));
            Assert.That(detail.AssetCount, Is.EqualTo(1));
            Assert.That(detail.Assets, Has.Count.EqualTo(1));
            Assert.That(detail.Assets[0].RelativePath, Is.EqualTo("references/guide.md"));
        });
    }

    /// <summary>
    /// The client keys structured validation failures on 400 alone, and reads `errors` as a flat
    /// array. [ApiController]'s automatic response would be a dictionary-shaped
    /// ValidationProblemDetails, which fails to deserialise and surfaces as a raw JSON error.
    /// </summary>
    [Test]
    public async Task ImportSkill_RejectedPackage_Returns400WithFlatErrorArray()
    {
        var broken = new TestZipBuilder().AddFile("demo-skill/notes.md", "no manifest").Build();

        var result = await _controller.ImportSkill(AsFormFile(broken), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var payload = ((BadRequestObjectResult)result).Value as SkillImportErrorResponse;

        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Errors, Is.Not.Empty);
        Assert.That(payload.Errors.Any(e => e.Contains("SKILL.md", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task ImportSkill_NoFile_Returns400RatherThanThrowing()
    {
        var result = await _controller.ImportSkill(null, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var payload = ((BadRequestObjectResult)result).Value as SkillImportErrorResponse;
        Assert.That(payload!.Errors, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ImportSkill_EmptyFile_Returns400()
    {
        var result = await _controller.ImportSkill(AsFormFile([]), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    // ---------------------------------------------------------------- read

    [Test]
    public async Task GetSkills_ProjectsListRows()
    {
        await ImportAsync(("references/guide.md", "guide"));

        var result = await _controller.GetSkills(CancellationToken.None);
        var rows = ((OkObjectResult)result.Result!).Value as List<SkillListItem>;

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(rows![0].Name, Is.EqualTo("demo-skill"));
            Assert.That(rows[0].ImportedByEmail, Is.EqualTo("admin@test.com"));
            Assert.That(rows[0].AssetCount, Is.EqualTo(1));
            Assert.That(rows[0].Version, Is.EqualTo("1.0"));
        });
    }

    /// <summary>404 is contractual — the client maps it to "Skill not found", not to a failure.</summary>
    [Test]
    public async Task GetSkill_UnknownId_Returns404()
    {
        var result = await _controller.GetSkill(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    /// <summary>
    /// A skill with no version must not serialise null into the DTO's non-nullable string, which
    /// the UI would render as an empty badge.
    /// </summary>
    [Test]
    public async Task GetSkills_SkillWithoutVersion_RendersAPlaceholder()
    {
        const string noVersion = """
            ---
            name: demo-skill
            description: A skill with no version declared.
            ---

            # Demo
            """;

        await _controller.ImportSkill(
            AsFormFile(new TestZipBuilder().AddFile("demo-skill/SKILL.md", noVersion).Build()),
            CancellationToken.None);

        var result = await _controller.GetSkills(CancellationToken.None);
        var rows = ((OkObjectResult)result.Result!).Value as List<SkillListItem>;

        Assert.That(rows![0].Version, Is.Not.Null.And.Not.Empty);
    }

    // ---------------------------------------------------------------- delete

    /// <summary>
    /// Deleting an already-deleted skill is not an error. The client only checks for success, so a
    /// concurrent delete from a second tab would otherwise surface as "Deletion Failed".
    /// </summary>
    [Test]
    public async Task DeleteSkill_UnknownId_ReturnsNoContent()
    {
        var result = await _controller.DeleteSkill(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    // ---------------------------------------------------------------- export

    /// <summary>
    /// The export repacks SKILL.md from stored columns rather than keeping the original bytes, so
    /// the only meaningful assertion is that the result survives the importer — the same validation
    /// a user's own upload faces.
    /// </summary>
    [Test]
    public async Task ExportSkill_ProducesAPackageThatReImportsCleanly()
    {
        var imported = await ImportAsync(
            ("assets/note.txt", "asset body"), ("references/guide.md", "reference body"));

        var exported = await _controller.ExportSkill(imported.Id, CancellationToken.None);

        Assert.That(exported, Is.InstanceOf<FileContentResult>());
        var file = (FileContentResult)exported;

        Assert.Multiple(() =>
        {
            Assert.That(file.ContentType, Is.EqualTo("application/zip"));
            Assert.That(file.FileDownloadName, Is.EqualTo("demo-skill.zip"));
        });

        var reimported = new SkillPackageImporter().Import(file.FileContents, file.FileDownloadName, _userId);

        Assert.That(reimported.Errors, Is.Empty, string.Join(" | ", reimported.Errors));
        Assert.Multiple(() =>
        {
            Assert.That(reimported.Succeeded, Is.True);
            Assert.That(reimported.Skill!.Name, Is.EqualTo("demo-skill"));
            Assert.That(reimported.Skill.Description, Is.EqualTo(imported.Description));
            Assert.That(reimported.Skill.Version, Is.EqualTo("1.0"));
            Assert.That(reimported.Skill.License, Is.EqualTo("Apache-2.0"));
            Assert.That(reimported.Skill.Compatibility, Is.EqualTo("Requires nothing in particular."));
            Assert.That(
                reimported.Skill.Assets.Select(a => a.RelativePath).Order(StringComparer.Ordinal),
                Is.EqualTo(new[] { "assets/note.txt", "references/guide.md" }));
        });
    }

    [Test]
    public async Task ExportSkill_UnknownId_Returns404()
    {
        var result = await _controller.ExportSkill(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    // ---------------------------------------------------------------- agent bindings

    /// <summary>
    /// Must return every skill, not only bound ones — the UI renders one toggle per returned row,
    /// so omitting unbound skills would make them impossible to bind.
    /// </summary>
    [Test]
    public async Task GetAgentSkills_ReturnsAllSkillsWithBindingFlags()
    {
        await ImportAsync();
        var agentId = Guid.NewGuid();

        var result = await _controller.GetAgentSkills(agentId, CancellationToken.None);
        var bindings = ((OkObjectResult)result.Result!).Value as List<AgentSkillDto>;

        Assert.That(bindings, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(bindings![0].Name, Is.EqualTo("demo-skill"));
            Assert.That(bindings[0].IsEnabled, Is.False, "an unbound skill is listed as disabled, not omitted");
        });
    }

    [Test]
    public async Task UpdateAgentSkills_ThenGet_RoundTripsTheFlag()
    {
        var imported = await ImportAsync();
        var agentId = Guid.NewGuid();

        var update = await _controller.UpdateAgentSkills(
            agentId,
            [new AgentSkillDto { SkillId = imported.Id, IsEnabled = true }],
            CancellationToken.None);

        Assert.That(update, Is.InstanceOf<NoContentResult>());

        var result = await _controller.GetAgentSkills(agentId, CancellationToken.None);
        var bindings = ((OkObjectResult)result.Result!).Value as List<AgentSkillDto>;

        Assert.That(bindings![0].IsEnabled, Is.True);
    }

    [Test]
    public async Task UpdateAgentSkills_UnknownSkillId_Returns400()
    {
        var agentId = Guid.NewGuid();

        var result = await _controller.UpdateAgentSkills(
            agentId,
            [new AgentSkillDto { SkillId = Guid.NewGuid(), IsEnabled = true }],
            CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }
}
