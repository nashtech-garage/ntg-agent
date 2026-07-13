using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Skills;
using NTG.Agent.MCP.Server.Data;
using NTG.Agent.MCP.Server.Models;
using NTG.Agent.MCP.Server.Services;

namespace NTG.Agent.MCP.Server.Controllers;

/// <summary>
/// CRUD API for the skill catalog. Reached only through the Orchestrator's
/// admin-authorized proxy (internal service-to-service traffic).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SkillsController : ControllerBase
{
    private readonly SkillDbContext _dbContext;

    public SkillsController(SkillDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    [HttpGet]
    public async Task<ActionResult<List<SkillDto>>> GetSkills()
    {
        var skills = await _dbContext.Skills
            .OrderBy(s => s.Name)
            .Select(s => new SkillDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
            })
            .ToListAsync();

        return Ok(skills);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SkillDetailDto>> GetSkill(Guid id)
    {
        var skill = await _dbContext.Skills.FindAsync(id);
        if (skill is null)
        {
            return NotFound();
        }

        return Ok(ToDetailDto(skill));
    }

    [HttpPost]
    public async Task<ActionResult<SkillDetailDto>> CreateSkill([FromBody] SaveSkillRequest request)
    {
        if (!SkillMarkdownParser.TryParse(request.Content, out var frontmatter, out var error))
        {
            return BadRequest(error);
        }

        if (await _dbContext.Skills.AnyAsync(s => s.Name == frontmatter!.Name))
        {
            return Conflict($"A skill named '{frontmatter!.Name}' already exists.");
        }

        var skill = new Skill
        {
            Name = frontmatter!.Name,
            Description = frontmatter.Description,
            Content = request.Content,
        };

        _dbContext.Skills.Add(skill);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSkill), new { id = skill.Id }, ToDetailDto(skill));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SkillDetailDto>> UpdateSkill(Guid id, [FromBody] SaveSkillRequest request)
    {
        var skill = await _dbContext.Skills.FindAsync(id);
        if (skill is null)
        {
            return NotFound();
        }

        if (!SkillMarkdownParser.TryParse(request.Content, out var frontmatter, out var error))
        {
            return BadRequest(error);
        }

        if (await _dbContext.Skills.AnyAsync(s => s.Name == frontmatter!.Name && s.Id != id))
        {
            return Conflict($"A skill named '{frontmatter!.Name}' already exists.");
        }

        skill.Name = frontmatter!.Name;
        skill.Description = frontmatter.Description;
        skill.Content = request.Content;
        skill.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(ToDetailDto(skill));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSkill(Guid id)
    {
        var skill = await _dbContext.Skills.FindAsync(id);
        if (skill is null)
        {
            return NotFound();
        }

        _dbContext.Skills.Remove(skill);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    private static SkillDetailDto ToDetailDto(Skill skill) => new()
    {
        Id = skill.Id,
        Name = skill.Name,
        Description = skill.Description,
        Content = skill.Content,
        CreatedAt = skill.CreatedAt,
        UpdatedAt = skill.UpdatedAt,
    };
}
