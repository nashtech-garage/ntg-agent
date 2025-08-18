using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Tags;
using NTG.Agent.ServiceDefaults.Logging;

namespace NTG.Agent.Orchestrator.Controllers;
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class TagsController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    private readonly IApplicationLogger<TagsController> _logger;

    public TagsController(AgentDbContext agentDbContext, IApplicationLogger<TagsController> logger)
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // GET /api/tags?q=foo
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TagDto>>> GetTags([FromQuery] string? q, CancellationToken ct)
    {
        var query = _agentDbContext.Tags.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(t => EF.Functions.Like(t.Name, $"%{q}%"));

        var items = await query
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);

        return Ok(items);
    }

    // GET /api/tags/{id}
    [HttpGet("{id:guid}", Name = nameof(GetTagById))]
    public async Task<ActionResult<TagDto>> GetTagById(Guid id, CancellationToken ct)
    {
        var tag = await _agentDbContext.Tags.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TagDto(t.Id, t.Name, t.CreatedAt, t.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return tag is null ? NotFound() : Ok(tag);
    }

    // POST /api/tags
    [HttpPost]
    public async Task<ActionResult<TagDto>> CreateTag([FromBody] TagCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        var name = dto.Name.Trim();

        var exists = await _agentDbContext.Tags.AnyAsync(t => t.Name == name, ct);
        if (exists) return Conflict($"Tag with name '{name}' already exists.");

        var entity = new Tag { Name = name };
        _agentDbContext.Tags.Add(entity);
        await _agentDbContext.SaveChangesAsync(ct);

        var result = new TagDto(entity.Id, entity.Name, entity.CreatedAt, entity.UpdatedAt);
        return CreatedAtRoute(nameof(GetTagById), new { id = entity.Id }, result);
    }

    // PUT /api/tags/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTag(Guid id, [FromBody] TagUpdateDto dto, CancellationToken ct)
    {
        var entity = await _agentDbContext.Tags.FindAsync([id], ct);
        if (entity is null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        var name = dto.Name.Trim();

        if (!string.Equals(entity.Name, name, StringComparison.Ordinal))
        {
            var nameTaken = await _agentDbContext.Tags.AnyAsync(t => t.Name == name && t.Id != id, ct);
            if (nameTaken) return Conflict($"Tag with name '{name}' already exists.");
        }

        entity.Name = name;
        await _agentDbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/tags/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTag(Guid id, CancellationToken ct)
    {
        var entity = await _agentDbContext.Tags.FindAsync([id], ct);
        if (entity is null) return NotFound();

        _agentDbContext.Tags.Remove(entity); // TagRoles will cascade if configured
        await _agentDbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    // -----------------------------
    // TagRoles - nested under Tag
    // -----------------------------

    // GET /api/tags/{tagId}/roles
    [HttpGet("{tagId:guid}/roles")]
    public async Task<ActionResult<IEnumerable<TagRoleDto>>> GetRolesForTag(Guid tagId, CancellationToken ct)
    {
        var tagExists = await _agentDbContext.Tags.AsNoTracking().AnyAsync(t => t.Id == tagId, ct);
        if (!tagExists) return NotFound($"Tag {tagId} not found.");

        var items = await _agentDbContext.TagRoles.AsNoTracking()
            .Where(x => x.TagId == tagId)
            .OrderBy(x => x.RoleId)
            .Select(x => new TagRoleDto(x.Id, x.TagId, x.RoleId, x.CreatedAt, x.UpdatedAt))
            .ToListAsync(ct);

        return Ok(items);
    }

    // POST /api/tags/{tagId}/roles
    // Body: { "roleId": "..." }
    [HttpPost("{tagId:guid}/roles")]
    public async Task<ActionResult<TagRoleDto>> AttachRoleToTag(Guid tagId, [FromBody] TagRoleAttachDto dto, CancellationToken ct)
    {
        // Validate Tag
        var tagExists = await _agentDbContext.Tags.AnyAsync(t => t.Id == tagId, ct);
        if (!tagExists) return NotFound($"Tag {tagId} not found.");

        // Enforce uniqueness (also guaranteed by unique index)
        var exists = await _agentDbContext.TagRoles.AnyAsync(x => x.TagId == tagId && x.RoleId == dto.RoleId, ct);
        if (exists) return Conflict("This tag/role mapping already exists.");

        var entity = new TagRole
        {
            TagId = tagId,
            RoleId = dto.RoleId
        };

        _agentDbContext.TagRoles.Add(entity);
        await _agentDbContext.SaveChangesAsync(ct);

        var result = new TagRoleDto(entity.Id, entity.TagId, entity.RoleId, entity.CreatedAt, entity.UpdatedAt);
        // Optional: return Location header to the roles collection
        return CreatedAtAction(nameof(GetRolesForTag), new { tagId }, result);
    }

    // DELETE /api/tags/{tagId}/roles/{roleId}
    [HttpDelete("{tagId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> DetachRoleFromTag(Guid tagId, Guid roleId, CancellationToken ct)
    {
        var entity = await _agentDbContext.TagRoles
            .FirstOrDefaultAsync(x => x.TagId == tagId && x.RoleId == roleId, ct);

        if (entity is null) return NotFound();

        _agentDbContext.TagRoles.Remove(entity);
        await _agentDbContext.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record TagCreateDto(string Name);
public record TagUpdateDto(string Name);
public record TagDto(Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt);

public record TagRoleAttachDto(Guid RoleId);
public record TagRoleDto(Guid Id, Guid TagId, Guid RoleId, DateTime CreatedAt, DateTime UpdatedAt);

