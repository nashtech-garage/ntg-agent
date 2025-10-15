using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Shared.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
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
            .Select(x => new AgentDetail(x.Id, x.Name, x.Instructions, x.ProviderName, x.ProviderEndpoint, x.ProviderApiKey, x.ProviderModelName))
            .FirstOrDefaultAsync();
        if (agent == null)
        {
            return NotFound();
        }
        return Ok(agent);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAgent(Guid id, [FromBody] AgentDetail updatedAgent)
    {
        if (id != updatedAgent.Id)
        {
            return BadRequest("ID in URL does not match ID in body.");
        }
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var agent = await _agentDbContext.Agents.FindAsync(id);
        if (agent == null)
        {
            return NotFound();
        }
        agent.Name = updatedAgent.Name;
        agent.Instructions = updatedAgent.Instructions;
        agent.ProviderName = updatedAgent.ProviderName;
        agent.ProviderEndpoint = updatedAgent.ProviderEndpoint;
        agent.ProviderApiKey = updatedAgent.ProviderApiKey;
        agent.ProviderModelName = updatedAgent.ProviderModelName;
        agent.UpdatedAt = DateTime.UtcNow;
        agent.UpdatedByUserId = userId;
        await _agentDbContext.SaveChangesAsync();
        return NoContent();
    }
}
