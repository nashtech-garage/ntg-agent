using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Services.Agents;
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

    /// <summary>
    /// Retrieves a list of all agents, optionally filtered by AgentKind.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAgents([FromQuery] AgentKind? agentKind = null)
    {
        IQueryable<Models.Agents.Agent> query = _agentDbContext.Agents;
        if (agentKind.HasValue)
        {
            query = query.Where(x => x.AgentKind == agentKind.Value);
        }

        var agents = await query
            .Select(x => new AgentListItem(x.Id, x.Name, x.OwnerUser.Email, x.UpdatedByUser.Email, x.UpdatedAt, x.IsDefault, x.IsPublished, x.AgentKind))
            .ToListAsync();
        return Ok(agents);
    }

    /// <summary>
    /// Retrieves detailed information about a specific agent by its unique identifier.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAgentById(Guid id)
    {
        var agent = await _agentDbContext.Agents
            .Where(x => x.Id == id)
            .Include(x => x.AgentTools)
            .Select(x => new AgentDetail(x.Id, x.Name, x.Instructions, x.ProviderName, x.ProviderEndpoint, x.ProviderApiKey, x.ProviderModelName)
            {
                Description = x.Description,
                McpServer = x.McpServer,
                ToolCount = $"{x.AgentTools.Count(a => a.IsEnabled)}/{x.AgentTools.Count}",
                IsDefault = x.IsDefault,
                IsPublished = x.IsPublished,
                AgentKind = x.AgentKind,
                Mode = x.Mode
            })
            .FirstOrDefaultAsync();

        if (agent == null)
        {
            return NotFound();
        }
        return Ok(agent);
    }

    /// <summary>
    /// Updates an existing agent's configuration and settings.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAgent(Guid id, [FromBody] AgentDetail updatedAgent)
    {
        if (id != updatedAgent.Id)
        {
            return BadRequest("ID in URL does not match ID in body.");
        }
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var agent = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == id);
        if (agent == null)
        {
            return NotFound();
        }
        agent.Name = updatedAgent.Name;
        agent.Description = updatedAgent.Description;
        agent.Instructions = updatedAgent.Instructions ?? string.Empty;
        agent.ProviderName = updatedAgent.ProviderName ?? string.Empty;
        agent.ProviderEndpoint = updatedAgent.ProviderEndpoint ?? string.Empty;
        agent.ProviderApiKey = updatedAgent.ProviderApiKey ?? string.Empty;
        agent.ProviderModelName = updatedAgent.ProviderModelName ?? string.Empty;
        agent.McpServer = updatedAgent.McpServer;
        agent.Mode = updatedAgent.Mode;
        agent.UpdatedAt = DateTime.UtcNow;
        agent.UpdatedByUserId = userId;
        await _agentDbContext.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Retrieves all available tools for a specific agent, including their enabled status.
    /// </summary>
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

    /// <summary>
    /// Updates the tool configuration for a specific agent.
    /// </summary>
    [HttpPut("{id}/tools")]
    public async Task<IActionResult> UpdateAgentTools(Guid id, [FromBody] List<AgentToolDto> updatedTools)
    {
        var agent = await _agentDbContext.Agents
            .Include(a => a.AgentTools)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (agent == null)
            return NotFound($"Agent with ID '{id}' not found.");

        var existingTools = agent.AgentTools.ToList();

        foreach (var toolDto in updatedTools)
        {
            var existingTool = existingTools.FirstOrDefault(t => t.Name == toolDto.Name);

            if (existingTool != null)
            {
                existingTool.IsEnabled = toolDto.IsEnabled;
                existingTool.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
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


    /// <summary>
    /// Connects an agent to a Model Context Protocol (MCP) server and retrieves available tools.
    /// </summary>
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

    /// <summary>
    /// Creates a new agent with the specified configuration.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateAgent([FromBody] AgentDetail updatedAgent)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        if (updatedAgent == null)
        {
            return BadRequest("Invalid agent data.");
        }

        var agent = new Models.Agents.Agent
        {
            Id = Guid.NewGuid(),
            Name = updatedAgent.Name,
            Description = updatedAgent.Description,
            Instructions = updatedAgent.Instructions ?? string.Empty,
            ProviderName = updatedAgent.ProviderName ?? string.Empty,
            McpServer = updatedAgent.McpServer,
            ProviderEndpoint = updatedAgent.ProviderEndpoint ?? string.Empty,
            ProviderApiKey = updatedAgent.ProviderApiKey ?? string.Empty,
            ProviderModelName = updatedAgent.ProviderModelName ?? string.Empty,
            Mode = updatedAgent.AgentKind == AgentKind.Inner ? AgentMode.Fast : updatedAgent.Mode,
            UpdatedByUserId = userId,
            OwnerUserId = userId,
            IsDefault = false,
            IsPublished = false,
            AgentKind = updatedAgent.AgentKind,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _agentDbContext.Agents.Add(agent);
        await _agentDbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAgentById), new { id = agent.Id }, agent.Id);
    }

    /// <summary>
    /// Updates the publication status of an agent.
    /// </summary>
    [HttpPatch("{id}/publish")]
    public async Task<IActionResult> UpdateAgentPublishStatus(Guid id, [FromBody] bool isPublished)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var agent = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == id);
        
        if (agent == null)
        {
            return NotFound($"Agent with ID '{id}' not found.");
        }

        agent.IsPublished = isPublished;
        agent.UpdatedAt = DateTime.UtcNow;
        agent.UpdatedByUserId = userId;
        
        await _agentDbContext.SaveChangesAsync();

        return Ok(new { message = $"Agent successfully {(isPublished ? "published" : "unpublished")}.", isPublished = agent.IsPublished });
    }

    /// <summary>
    /// Deletes the agent with the specified identifier.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(Guid id)
    {
        var agent = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == id);
        if (agent == null)
        {
            return NotFound();
        }

        if (agent.IsDefault)
        {
            return BadRequest("Default agent cannot be deleted.");
        }

        if (agent.AgentKind == AgentKind.Inner)
        {
            var bindings = await _agentDbContext.AgentInnerAgents
                .Where(b => b.InnerAgentId == id)
                .ToListAsync();
            _agentDbContext.AgentInnerAgents.RemoveRange(bindings);
        }
        else
        {
            var associatedDocs = await _agentDbContext.Documents.AnyAsync(d => d.AgentId == id);
            if (associatedDocs)
            {
                return BadRequest("Agent cannot be deleted because it is associated with documents.");
            }
        }

        _agentDbContext.Agents.Remove(agent);
        await _agentDbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Retrieves inner agent bindings for an outer agent.
    /// </summary>
    [HttpGet("{id}/inner-agents")]
    public async Task<IActionResult> GetInnerAgentBindings(Guid id)
    {
        var outerAgentExists = await _agentDbContext.Agents.AnyAsync(a => a.Id == id && a.AgentKind == AgentKind.Outer);
        if (!outerAgentExists)
        {
            return NotFound($"Agent with ID '{id}' not found.");
        }

        var innerAgents = await _agentDbContext.Agents
            .Where(a => a.AgentKind == AgentKind.Inner && a.IsPublished)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Description,
                a.Instructions,
                a.ProviderModelName
            })
            .ToListAsync();

        var bindings = await _agentDbContext.AgentInnerAgents
            .Where(b => b.OuterAgentId == id)
            .ToListAsync();

        var bindingMap = bindings.ToDictionary(b => b.InnerAgentId, b => b.IsEnabled);

        var result = innerAgents.Select(agent => new InnerAgentBindingDto
        {
            InnerAgentId = agent.Id,
            Name = agent.Name,
            Description = string.IsNullOrWhiteSpace(agent.Description) ? agent.Instructions ?? string.Empty : agent.Description,
            ProviderModelName = agent.ProviderModelName ?? string.Empty,
            IsEnabled = bindingMap.TryGetValue(agent.Id, out var enabled) && enabled
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Updates inner agent bindings for an outer agent.
    /// </summary>
    [HttpPut("{id}/inner-agents")]
    public async Task<IActionResult> UpdateInnerAgentBindings(Guid id, [FromBody] List<InnerAgentBindingDto> bindings)
    {
        var outerAgent = await _agentDbContext.Agents
            .Include(a => a.InnerAgentBindings)
            .FirstOrDefaultAsync(a => a.Id == id && a.AgentKind == AgentKind.Outer);

        if (outerAgent == null)
        {
            return NotFound($"Agent with ID '{id}' not found.");
        }

        var requestedIds = bindings.Select(b => b.InnerAgentId).Distinct().ToList();
        var validIds = await _agentDbContext.Agents
            .Where(a => requestedIds.Contains(a.Id) && a.AgentKind == AgentKind.Inner)
            .Select(a => a.Id)
            .ToListAsync();

        if (validIds.Count != requestedIds.Count)
        {
            return BadRequest("One or more inner agents are invalid.");
        }

        var now = DateTime.UtcNow;
        var existingBindings = outerAgent.InnerAgentBindings.ToDictionary(b => b.InnerAgentId);

        foreach (var bindingDto in bindings)
        {
            if (existingBindings.TryGetValue(bindingDto.InnerAgentId, out var binding))
            {
                binding.IsEnabled = bindingDto.IsEnabled;
                binding.UpdatedAt = now;
            }
            else
            {
                outerAgent.InnerAgentBindings.Add(new Models.Agents.AgentInnerAgent
                {
                    OuterAgentId = id,
                    InnerAgentId = bindingDto.InnerAgentId,
                    IsEnabled = bindingDto.IsEnabled,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        var draftIds = await _agentDbContext.Agents
            .Where(a => a.AgentKind == AgentKind.Inner && !a.IsPublished)
            .Select(a => a.Id)
            .ToListAsync();

        var removed = outerAgent.InnerAgentBindings
            .Where(b => !requestedIds.Contains(b.InnerAgentId) && !draftIds.Contains(b.InnerAgentId))
            .ToList();

        if (removed.Count > 0)
        {
            _agentDbContext.AgentInnerAgents.RemoveRange(removed);
        }

        await _agentDbContext.SaveChangesAsync();

        return Ok("Inner agent bindings updated successfully.");
    }
}