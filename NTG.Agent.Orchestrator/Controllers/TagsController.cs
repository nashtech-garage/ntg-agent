using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Constants;
using NTG.Agent.Common.Dtos.Tags;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.Orchestrator.Models.Tags;
using System.Reflection.Metadata;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// Controller for managing tags and their role assignments.
/// Requires Admin role authorization for all operations.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class TagsController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    private readonly ILogger<TagsController> _logger;

    /// <summary>
    /// Initializes a new instance of the TagsController.
    /// </summary>
    /// <param name="agentDbContext">The database context for agent operations.</param>
    /// <param name="logger">The logger instance for logging operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public TagsController(AgentDbContext agentDbContext, ILogger<TagsController> logger)
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves all tags with optional search filtering.
    /// </summary>
    /// <param name="q">Optional search query to filter tags by name (case-insensitive).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A list of tags matching the search criteria, ordered by name.</returns>
    /// <response code="200">Returns the list of tags.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    // GET /api/tags?q=foo
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TagDto>>> GetTags([FromQuery] Guid agentId, [FromQuery] string? q, CancellationToken ct)
    {
        var query = _agentDbContext.Tags.AsNoTracking().AsQueryable();
        query = query.Where(t => t.AgentId == agentId);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(t => t.Name.Contains(q));

        var items = await query
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(
                t.Id, 
                t.Name, 
                t.CreatedAt, 
                t.UpdatedAt,
                t.IsDefault,
                _agentDbContext.DocumentTags.Count(dt => dt.TagId == t.Id)
            ))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Retrieves a specific tag by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the tag.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The tag with the specified ID.</returns>
    /// <response code="200">Returns the requested tag.</response>
    /// <response code="404">If the tag with the specified ID is not found.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    // GET /api/tags/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<TagDto>> GetTagById(Guid id, CancellationToken ct)
    {
        var tag = await _agentDbContext.Tags.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TagDto(
                t.Id, 
                t.Name, 
                t.CreatedAt, 
                t.UpdatedAt, 
                t.IsDefault,
                _agentDbContext.DocumentTags.Count(dt => dt.TagId == t.Id)
            ))
            .FirstOrDefaultAsync(ct);

        return tag is null ? NotFound() : Ok(tag);
    }

    /// <summary>
    /// Creates a new tag with the specified name.
    /// </summary>
    /// <param name="dto">The data transfer object containing the tag creation details.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The newly created tag.</returns>
    /// <response code="201">Returns the newly created tag.</response>
    /// <response code="400">If the tag name is null, empty, or whitespace.</response>
    /// <response code="409">If a tag with the same name already exists.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    // POST /api/tags
    [HttpPost]
    public async Task<ActionResult<TagDto>> CreateTag([FromBody] TagCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Tag Name is required.");

        var name = dto.Name.Trim();

        var exists = await _agentDbContext.Tags.AnyAsync(t => t.Name == name && t.AgentId == dto.AgentId, ct);
        if (exists) return Conflict($"Tag with name '{name}' already exists.");

        var entity = new Tag { Name = name, AgentId = dto.AgentId };
        _agentDbContext.Tags.Add(entity);
        await _agentDbContext.SaveChangesAsync(ct);

        var documentCount = await _agentDbContext.DocumentTags.CountAsync(dt => dt.TagId == entity.Id, ct);
        var result = new TagDto(entity.Id, entity.Name, entity.CreatedAt, entity.UpdatedAt, entity.IsDefault, documentCount);
        return CreatedAtAction(nameof(GetTagById), new { id = entity.Id }, result);
    }

    /// <summary>
    /// Updates an existing tag's name.
    /// </summary>
    /// <param name="id">The unique identifier of the tag to update.</param>
    /// <param name="dto">The data transfer object containing the updated tag details.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>No content on successful update.</returns>
    /// <response code="204">The tag was successfully updated.</response>
    /// <response code="400">If the tag name is null, empty, or whitespace.</response>
    /// <response code="404">If the tag with the specified ID is not found.</response>
    /// <response code="409">If a tag with the new name already exists.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    // PUT /api/tags/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTag(Guid id, [FromBody] TagUpdateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Tag Name is required.");

        var entity = await _agentDbContext.Tags.FindAsync([id], ct);
        if (entity is null) return NotFound();

        if (entity.AgentId != dto.AgentId)
            return Forbid("You do not have permission to update this tag.");

        var name = dto.Name.Trim();

        if (!string.Equals(entity.Name, name, StringComparison.Ordinal))
        {
            var nameTaken = await _agentDbContext.Tags.AnyAsync(t => t.Name == name && t.Id != id && t.AgentId == dto.AgentId, ct);
            if (nameTaken) return Conflict($"Tag with name '{name}' already exists.");
        }

        entity.Name = name;
        await _agentDbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a tag and all its associated role mappings.
    /// </summary>
    /// <param name="id">The unique identifier of the tag to delete.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>No content on successful deletion.</returns>
    /// <response code="204">The tag was successfully deleted.</response>
    /// <response code="404">If the tag with the specified ID is not found.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    /// <summary>
    /// Deletes a tag by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the tag to delete.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>No content if the tag is successfully deleted.</returns>
    /// <response code="204">If the tag is successfully deleted.</response>
    /// <response code="400">If the tag is associated with documents and cannot be deleted.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    /// <response code="404">If the tag with the specified ID is not found.</response>
    // DELETE /api/tags/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTag(Guid id, CancellationToken ct)
    {
        var entity = await _agentDbContext.Tags.FindAsync([id], ct);
        if (entity is null) return NotFound();

        if (entity.IsDefault)
            return Forbid("Default tags cannot be deleted.");

        // Check if the tag is associated with any documents
        var hasDocuments = await _agentDbContext.DocumentTags.AnyAsync(dt => dt.TagId == id, ct);
        if (hasDocuments)
        {
            return BadRequest("Cannot delete tag. It is currently associated with one or more documents. Please remove the tag from all documents before deleting it.");
        }

        _agentDbContext.Tags.Remove(entity);
        await _agentDbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Retrieves all available roles in the system that can be assigned to tags.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A list of all available roles, ordered by name.</returns>
    /// <response code="200">Returns the list of available roles.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    // GET /api/tags/available-roles
    [HttpGet("available-roles")]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetAvailableRoles(CancellationToken ct)
    {
        var roles = await _agentDbContext.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name))
            .ToListAsync(ct);

        return Ok(roles);
    }

    /// <summary>
    /// Retrieves all roles assigned to a specific tag.
    /// </summary>
    /// <param name="tagId">The unique identifier of the tag.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A list of roles assigned to the specified tag.</returns>
    /// <response code="200">Returns the list of roles assigned to the tag.</response>
    /// <response code="404">If the tag with the specified ID is not found.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
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

    /// <summary>
    /// Assigns a role to a specific tag, creating a tag-role mapping.
    /// </summary>
    /// <param name="tagId">The unique identifier of the tag.</param>
    /// <param name="dto">The data transfer object containing the role ID to assign.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The newly created tag-role mapping.</returns>
    /// <response code="201">Returns the newly created tag-role mapping.</response>
    /// <response code="404">If the tag with the specified ID is not found.</response>
    /// <response code="409">If the tag-role mapping already exists.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    // POST /api/tags/{tagId}/roles
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

    /// <summary>
    /// Removes a role assignment from a specific tag, deleting the tag-role mapping.
    /// </summary>
    /// <param name="tagId">The unique identifier of the tag.</param>
    /// <param name="roleId">The unique identifier of the role to unassign.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>No content on successful removal.</returns>
    /// <response code="204">The role was successfully removed from the tag.</response>
    /// <response code="404">If the tag-role mapping is not found.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    // DELETE /api/tags/{tagId}/roles/{roleId}
    [HttpDelete("{tagId:guid}/roles/{roleId}")]
    public async Task<IActionResult> DetachRoleFromTag(Guid tagId, Guid roleId, CancellationToken ct)
    {
        var entity = await _agentDbContext.TagRoles
            .FirstOrDefaultAsync(x => x.TagId == tagId && x.RoleId == roleId, ct);

        if (entity is null) return NotFound();

        _agentDbContext.TagRoles.Remove(entity);
        await _agentDbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Creates default tags for a specific agent.
    /// Creates a "Public" tag with anonymous role assignment.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <returns>No content on successful creation.</returns>
    /// <response code="200">The default tags were successfully created.</response>
    /// <response code="400">If default tags already exist for the agent.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have Admin role.</response>
    [HttpPost("{agentId}/default")]
    public async Task<ActionResult<TagDto>> CreateDefaultTagsForAgent(Guid agentId)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        var isTagExists = await _agentDbContext.Tags.AnyAsync(f => f.AgentId == agentId && f.IsDefault);
        if (isTagExists)
        {
            return BadRequest("Default Tags already exists for this agent.");
        }

        // Create Default Tags
        var tag = new Tag
        {
            Name = "Public",
            AgentId = agentId,
            IsDefault = true,
            Id = Guid.NewGuid()
        };
        _agentDbContext.Tags.Add(tag);
        // Create Default TagRoles
        var tagRole = new TagRole
        {
            TagId = tag.Id,
            RoleId = new Guid(Constants.AnonymousRoleId),
            Id = Guid.NewGuid()
        };
        _agentDbContext.TagRoles.Add(tagRole);

        await _agentDbContext.SaveChangesAsync();

        return Ok();
    }
}

