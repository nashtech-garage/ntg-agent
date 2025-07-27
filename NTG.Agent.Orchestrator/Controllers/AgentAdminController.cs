using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Shared.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class AgentAdminController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;

    public AgentAdminController(AgentDbContext agentDbContext)
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
    }

    [HttpGet]
    public async Task<IActionResult> GetAgents()
    {
        var agents = await _agentDbContext.Agents
            .Select(x => new AgentListItem(x.Id, x.Name, x.OwnerUser.Email, x.UpdatedByUser.Email, x.UpdatedAt))
            .ToListAsync();
        return Ok(agents);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAgentById(Guid id)
    {
        var agent = await _agentDbContext.Agents
            .Where(x => x.Id == id)
            .Select(x => new AgentDetail(x.Id, x.Name, x.Instructions))
            .FirstOrDefaultAsync();
        if (agent == null)
        {
            return NotFound();
        }
        return Ok(agent);
    }
}
