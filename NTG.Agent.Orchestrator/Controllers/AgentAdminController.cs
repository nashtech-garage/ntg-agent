using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AgentAdminController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    private readonly IAgentFactory _agentFactory;

    public AgentAdminController(AgentDbContext agentDbContext,
        IAgentFactory agentFactory
        )
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
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
            .Include(x => x.AgentTools)
            .Select(x => new AgentDetail(x.Id, x.Name, x.Instructions, x.ProviderName, x.ProviderEndpoint, x.ProviderApiKey, x.ProviderModelName)
            {
                McpServer = x.McpServer,
                ToolCount = $"{x.AgentTools.Count(a => a.IsEnabled)}/{x.AgentTools.Count}"
            })
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
        agent.McpServer = updatedAgent.McpServer;
        agent.UpdatedAt = DateTime.UtcNow;
        agent.UpdatedByUserId = userId;
        await _agentDbContext.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id}/tools")]
    public async Task<IActionResult> GetAgentToolsByAgentId(Guid id)
    {
        var agent = await _agentDbContext.Agents
            .Include(agent => agent.AgentTools)
            .FirstOrDefaultAsync(a => a.Id == id) ?? throw new ArgumentException($"Agent with ID '{id}' not found.");

        var availableTools = await _agentFactory.GetAvailableTools(agent);
        List<AgentToolDto> tools = MergeAgentTools(agent, availableTools);

        if (tools == null)
        {
            return NotFound();
        }
        return Ok(tools);
    }

    private static List<AgentToolDto> MergeAgentTools(Models.Agents.Agent agent, List<AITool> availableTools)
    {
        return availableTools
                .Select(t =>
                {
                    var existing = agent.AgentTools
                        .FirstOrDefault(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));

                    return new AgentToolDto
                    {
                        Id = existing?.Id ?? Guid.Empty,
                        AgentId = agent.Id,
                        Name = t.Name,
                        Description = t.Description ?? string.Empty,
                        AgentToolType = existing?.AgentToolType ?? AgentToolType.BuiltIn,
                        IsEnabled = existing?.IsEnabled ?? false,
                        CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
                        UpdatedAt = existing?.UpdatedAt ?? DateTime.UtcNow
                    };
                })
                .ToList();
    }

    [HttpPut("{id}/tools")]
    public async Task<IActionResult> UpdateAgentTools(Guid id, [FromBody] List<AgentToolDto> updatedTools)
    {
        var agent = await _agentDbContext.Agents
            .Include(a => a.AgentTools)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (agent == null)
            return NotFound($"Agent with ID '{id}' not found.");

        // Existing tools from DB
        var existingTools = agent.AgentTools.ToList();

        // Update or insert
        foreach (var toolDto in updatedTools)
        {
            var existingTool = existingTools.FirstOrDefault(t => t.Name == toolDto.Name);

            if (existingTool != null)
            {
                // Update existing
                existingTool.IsEnabled = toolDto.IsEnabled;
                existingTool.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Add new
                agent.AgentTools.Add(new Models.Agents.AgentTools
                {
                    AgentId = id,
                    Name = toolDto.Name,
                    Description = toolDto.Description,
                    IsEnabled = toolDto.IsEnabled,
                    AgentToolType = toolDto.AgentToolType,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await _agentDbContext.SaveChangesAsync();

        return Ok("Agent tools updated successfully.");
    }


    [HttpPost("{id}/connect")]
    public async Task<IEnumerable<AgentToolDto>> ConnectToMcpServerAsync(Guid id, [FromBody] string endpoint)
    {
        var agent = await _agentDbContext.Agents
            .Include(agent => agent.AgentTools)
            .FirstOrDefaultAsync(a => a.Id == id) ?? throw new ArgumentException($"Agent with ID '{id}' not found.");

        var agentToolsDto = new List<AgentToolDto>();

        if (!string.IsNullOrEmpty(endpoint.Trim()))
        {
            var tools = await _agentFactory.GetMcpToolsAsync(endpoint);

            agentToolsDto = MergeAgentTools(agent, tools.ToList());
        }

        return agentToolsDto;
    }
}
