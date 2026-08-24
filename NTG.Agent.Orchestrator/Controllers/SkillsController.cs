using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Common.Dtos.Skills;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Services.Skills;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// Admin API for imported Agent Skills: import, list, inspect, export, delete, and bind to agents.
/// </summary>
/// <remarks>
/// <para>
/// A skill's body is injected into a model's context, so import is Admin-only. The
/// <c>[Authorize(Roles = "Admin")]</c> here is the real boundary — the matching attribute on the
/// Blazor page is UI gating only, and the WASM client's <c>AddAuthorizationCore()</c> does not
/// enforce roles along the interactive path.
/// </para>
/// <para>
/// Every validation failure returns <c>400</c> with a <see cref="SkillImportErrorResponse"/>, whose
/// <c>errors</c> is a flat array of strings. This matters more than it looks: the client
/// deserialises a success response into <see cref="SkillDetail"/>, and a record with all-default
/// members deserialises from an error payload <em>without throwing</em> — so returning 200 with an
/// error body would render a confident "Imported '' successfully". The status code is the only
/// thing separating the two paths.
/// </para>
/// </remarks>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class SkillsController(SkillRegistry skillRegistry, ILogger<SkillsController> logger) : ControllerBase
{
    private readonly SkillRegistry _skillRegistry = skillRegistry ?? throw new ArgumentNullException(nameof(skillRegistry));
    private readonly ILogger<SkillsController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Rendered where a skill has no version, so the UI never shows a bare badge.</summary>
    private const string UnknownVersion = "—";

    [HttpGet]
    public async Task<ActionResult<IList<SkillListItem>>> GetSkills(CancellationToken cancellationToken)
    {
        var skills = await _skillRegistry.ListAsync(cancellationToken);

        return Ok(skills
            .Select(s => new SkillListItem(
                s.Id,
                s.Name,
                s.Description,
                s.Version ?? UnknownVersion,
                s.ImportedByEmail,
                s.CreatedAt,
                s.AssetCount))
            .ToList());
    }

    /// <summary>
    /// A single skill. <c>404</c> is contractual: the client maps it to "Skill not found. It may
    /// have been deleted." rather than treating it as a transport failure.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SkillDetail>> GetSkill(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _skillRegistry.GetAsync(id, cancellationToken);

        return detail is null ? NotFound() : Ok(ToDetail(detail));
    }

    /// <summary>
    /// Imports a skill package. The uploaded file is read fully into memory, bounded by
    /// <see cref="SkillPackageImporter.MaxPackageBytes"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="file"/> is nullable and null-checked by hand rather than annotated
    /// <c>[Required]</c>. <c>[ApiController]</c>'s automatic model-state response is
    /// <c>ValidationProblemDetails</c>, whose <c>errors</c> is a dictionary — the client expects an
    /// array and would surface a raw deserialisation error instead of the reason.
    /// </para>
    /// <para>
    /// <see cref="RequestSizeLimitAttribute"/> is the first control in the import chain and, at the
    /// time of writing, the only server-side upload limit in the solution; the browser-side
    /// constants elsewhere constrain nothing but the browser.
    /// </para>
    /// </remarks>
    [HttpPost("import")]
    [RequestSizeLimit(SkillPackageImporter.MaxPackageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = SkillPackageImporter.MaxPackageBytes)]
    public async Task<IActionResult> ImportSkill(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Rejected("No file was uploaded.");
        }

        if (file.Length > SkillPackageImporter.MaxPackageBytes)
        {
            return Rejected(
                $"Package is {file.Length:N0} bytes; the limit is {SkillPackageImporter.MaxPackageBytes:N0}.");
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        byte[] packageBytes;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, cancellationToken);
            packageBytes = buffer.ToArray();
        }

        var outcome = await _skillRegistry.ImportAsync(packageBytes, file.FileName, userId, cancellationToken);

        if (!outcome.Succeeded)
        {
            return BadRequest(new SkillImportErrorResponse(outcome.Errors.ToList()));
        }

        // Re-read rather than projecting the outcome: the client deserialises this body as
        // SkillDetail, and anything narrower would bind to defaults and display as a blank skill.
        var detail = await _skillRegistry.GetAsync(outcome.SkillId!.Value, cancellationToken);
        if (detail is null)
        {
            _logger.LogError("Skill {SkillId} vanished between import and read-back", outcome.SkillId);
            return Rejected("The skill was imported but could not be read back.");
        }

        return Ok(ToDetail(detail, outcome.Replaced));
    }

    /// <summary>
    /// Deletes a skill. Returns <c>204</c> even when it does not exist: the client only checks for
    /// success, and a concurrent delete from another tab should not surface as "Deletion Failed".
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSkill(Guid id, CancellationToken cancellationToken)
    {
        await _skillRegistry.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Repacks a stored skill into the same layout it was imported from, so an exported package
    /// re-imports cleanly.
    /// </summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> ExportSkill(Guid id, CancellationToken cancellationToken)
    {
        var skill = await _skillRegistry.GetForExportAsync(id, cancellationToken);
        if (skill is null)
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(
                archive, $"{skill.Name}/SKILL.md", Encoding.UTF8.GetBytes(BuildManifest(skill)), cancellationToken);

            foreach (var asset in skill.Assets.OrderBy(a => a.RelativePath, StringComparer.Ordinal))
            {
                await WriteEntryAsync(archive, $"{skill.Name}/{asset.RelativePath}", asset.Content, cancellationToken);
            }
        }

        return File(buffer.ToArray(), "application/zip", $"{skill.Name}.zip");
    }

    [HttpGet("agents/{agentId:guid}/skills")]
    public async Task<ActionResult<IList<AgentSkillDto>>> GetAgentSkills(Guid agentId, CancellationToken cancellationToken)
    {
        var bindings = await _skillRegistry.GetAgentBindingsAsync(agentId, cancellationToken);

        return Ok(bindings
            .Select(b => new AgentSkillDto
            {
                SkillId = b.SkillId,
                Name = b.Name,
                Description = b.Description,
                IsEnabled = b.IsEnabled,
            })
            .ToList());
    }

    [HttpPut("agents/{agentId:guid}/skills")]
    public async Task<IActionResult> UpdateAgentSkills(
        Guid agentId, [FromBody] List<AgentSkillDto> bindings, CancellationToken cancellationToken)
    {
        if (bindings is null)
        {
            return Rejected("No bindings were supplied.");
        }

        // Name and Description are echoed back by the client for display; only the id and the flag
        // are authoritative.
        var applied = await _skillRegistry.SetAgentBindingsAsync(
            agentId,
            bindings.Select(b => (b.SkillId, b.IsEnabled)).ToList(),
            cancellationToken);

        return applied ? NoContent() : Rejected("One or more skills are invalid.");
    }

    private BadRequestObjectResult Rejected(params string[] errors) =>
        BadRequest(new SkillImportErrorResponse(errors.ToList()));

    private static SkillDetail ToDetail(SkillRegistry.SkillDetailView detail, bool replaced = false) =>
        new(
            detail.Id,
            detail.Name,
            detail.Description,
            detail.Version ?? UnknownVersion,
            detail.ImportedByEmail,
            detail.CreatedAt,
            detail.AssetCount,
            detail.Body,
            detail.Assets.Select(a => new SkillAssetDto(a.RelativePath, a.SizeBytes)).ToList(),
            replaced);

    /// <summary>
    /// Reassembles <c>SKILL.md</c> from the stored frontmatter fields and body.
    /// </summary>
    /// <remarks>
    /// Only <c>metadata.version</c> is quoted; <c>name</c>, <c>description</c>, <c>license</c> and
    /// <c>compatibility</c> are emitted as bare scalars. That is safe rather than accidental — the
    /// importer rejects newlines and structural markers in those fields, so no value can break out
    /// of its line. It does mean a value containing <c>": "</c> round-trips through this project's
    /// own parser but may be read as a nested mapping by a strict YAML parser, so quote them here
    /// if exported packages ever need to survive a third-party reader.
    /// </remarks>
    private static string BuildManifest(Models.Skills.Skill skill)
    {
        var builder = new StringBuilder();

        builder.Append("---\n");
        builder.Append(CultureInfo.InvariantCulture, $"name: {skill.Name}\n");
        builder.Append(CultureInfo.InvariantCulture, $"description: {skill.Description}\n");

        if (!string.IsNullOrWhiteSpace(skill.License))
        {
            builder.Append(CultureInfo.InvariantCulture, $"license: {skill.License}\n");
        }

        if (!string.IsNullOrWhiteSpace(skill.Compatibility))
        {
            builder.Append(CultureInfo.InvariantCulture, $"compatibility: {skill.Compatibility}\n");
        }

        if (!string.IsNullOrWhiteSpace(skill.Version))
        {
            builder.Append("metadata:\n");
            builder.Append(CultureInfo.InvariantCulture, $"  version: \"{skill.Version}\"\n");
        }

        builder.Append("---\n\n");
        builder.Append(skill.Body);

        return builder.ToString();
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive, string entryName, byte[] content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken);
    }
}
