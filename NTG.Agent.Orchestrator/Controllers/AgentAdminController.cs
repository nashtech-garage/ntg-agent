using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Services;
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
    private readonly ModelDiscoveryService _modelDiscoveryService;

    public AgentAdminController(AgentDbContext agentDbContext,
        IAgentFactory agentFactory,
        ModelDiscoveryService modelDiscoveryService
        )
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _modelDiscoveryService = modelDiscoveryService ?? throw new ArgumentNullException(nameof(modelDiscoveryService));
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
            .Select(x => new AgentListItem(x.Id, x.Name, x.OwnerUser.Email, x.UpdatedByUser.Email, x.UpdatedAt, x.IsDefault, x.IsPublished, x.AgentKind, x.Provider != null ? x.Provider.Name : null))
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
            .Select(x => new AgentDetail
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                Instructions = x.Instructions,
                ProviderId = x.ProviderId,
                ProviderName = x.Provider != null ? x.Provider.Name : null,
                ProviderType = x.Provider != null ? x.Provider.ProviderType.ToDisplayName() : null,
                ModelOverride = x.ModelOverride,
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
        agent.ProviderId = updatedAgent.ProviderId;
        agent.ModelOverride = updatedAgent.ModelOverride;
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
        var mcpToolNames = await GetMcpToolNamesAsync(agent);
        List<AgentToolDto> tools = MergeAgentTools(agent, availableTools, mcpToolNames);

        if (tools == null)
        {
            return NotFound();
        }
        return Ok(tools);
    }

    // Names of the tools the agent's MCP server exposes, used to classify a tool as MCP vs built-in.
    private async Task<HashSet<string>> GetMcpToolNamesAsync(Models.Agents.Agent agent)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(agent.McpServer))
        {
            var mcpTools = await _agentFactory.GetMcpToolsAsync(agent.McpServer);
            foreach (var tool in mcpTools)
            {
                names.Add(tool.Name);
            }
        }
        return names;
    }

    private static List<AgentToolDto> MergeAgentTools(Models.Agents.Agent agent, List<AITool> availableTools, ISet<string> mcpToolNames)
    {
        return availableTools
                .Select(t =>
                {
                    var existing = agent.AgentTools
                        .FirstOrDefault(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));

                    // Classify by source: tools exposed by the MCP server are MCP, everything else is built-in.
                    var toolType = mcpToolNames.Contains(t.Name) ? AgentToolType.MCP : AgentToolType.BuiltIn;

                    return new AgentToolDto
                    {
                        Id = existing?.Id ?? Guid.Empty,
                        AgentId = agent.Id,
                        Name = t.Name,
                        Description = t.Description ?? string.Empty,
                        AgentToolType = toolType,
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

        // Collapse any pre-existing duplicate rows (same name) down to one, keeping a single tracked row
        // per tool. Self-heals agents whose tools were duplicated by earlier saves.
        var byName = new Dictionary<string, Models.Agents.AgentTools>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in agent.AgentTools.ToList())
        {
            if (byName.ContainsKey(tool.Name))
            {
                _agentDbContext.AgentTools.Remove(tool);
            }
            else
            {
                byName[tool.Name] = tool;
            }
        }

        // Upsert each incoming tool exactly once (ignore duplicate names within the payload).
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var toolDto in updatedTools)
        {
            if (string.IsNullOrWhiteSpace(toolDto.Name) || !processed.Add(toolDto.Name))
            {
                continue;
            }

            if (byName.TryGetValue(toolDto.Name, out var existingTool))
            {
                existingTool.IsEnabled = toolDto.IsEnabled;
                existingTool.AgentToolType = toolDto.AgentToolType;
                existingTool.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var added = new Models.Agents.AgentTools
                {
                    AgentId = id,
                    Name = toolDto.Name,
                    Description = toolDto.Description,
                    IsEnabled = toolDto.IsEnabled,
                    AgentToolType = toolDto.AgentToolType,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                agent.AgentTools.Add(added);
                byName[toolDto.Name] = added;
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
            var tools = (await _agentFactory.GetMcpToolsAsync(endpoint)).ToList();
            // Everything from the MCP server is, by definition, an MCP tool.
            var mcpToolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            agentToolsDto = MergeAgentTools(agent, tools, mcpToolNames);
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
            ProviderId = updatedAgent.ProviderId,
            ModelOverride = updatedAgent.ModelOverride,
            McpServer = updatedAgent.McpServer,
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
            .Include(a => a.Provider)
            .Where(a => a.AgentKind == AgentKind.Inner && a.IsPublished)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Description,
                a.Instructions,
                ProviderName = a.Provider != null ? a.Provider.Name : null,
                ModelOverride = a.ModelOverride ?? (a.Provider != null ? a.Provider.DefaultModel : null)
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
            ProviderName = agent.ProviderName,
            ModelOverride = agent.ModelOverride,
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

    /// <summary>
    /// Retrieves all providers.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders()
    {
        var providers = await _agentDbContext.Providers
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.ProviderType,
                p.Endpoint,
                p.ApiKey,
                p.DefaultModel,
                AgentCount = p.Agents.Count,
                p.CreatedAt,
                p.UpdatedAt
            })
            .ToListAsync();
        var result = providers.Select(p => new ProviderDto
        {
            Id = p.Id,
            Name = p.Name,
            ProviderType = p.ProviderType,
            Endpoint = p.Endpoint,
            ApiKey = p.ApiKey != null && p.ApiKey.Length > 12
                ? p.ApiKey[..6] + "***" + p.ApiKey[^4..]
                : p.ApiKey,
            DefaultModel = p.DefaultModel,
            AgentCount = p.AgentCount,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a specific provider by its ID.
    /// </summary>
    [HttpGet("providers/{id}")]
    public async Task<IActionResult> GetProvider(Guid id)
    {
        var provider = await _agentDbContext.Providers
            .Select(p => new ProviderDto
            {
                Id = p.Id,
                Name = p.Name,
                ProviderType = p.ProviderType,
                Endpoint = p.Endpoint,
                ApiKey = p.ApiKey,
                DefaultModel = p.DefaultModel,
                AgentCount = p.Agents.Count,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .FirstOrDefaultAsync(p => p.Id == id);

        if (provider == null) return NotFound();
        return Ok(provider);
    }

    /// <summary>
    /// Creates a new provider.
    /// </summary>
    [HttpPost("providers")]
    public async Task<IActionResult> CreateProvider([FromBody] ProviderDto dto)
    {
        var provider = new Models.Agents.Provider
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            ProviderType = dto.ProviderType,
            Endpoint = dto.Endpoint,
            ApiKey = dto.ApiKey,
            DefaultModel = dto.DefaultModel,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _agentDbContext.Providers.Add(provider);
        await _agentDbContext.SaveChangesAsync();
        return CreatedAtAction(nameof(GetProvider), new { id = provider.Id }, provider.Id);
    }

    /// <summary>
    /// Updates an existing provider.
    /// </summary>
    [HttpPut("providers/{id}")]
    public async Task<IActionResult> UpdateProvider(Guid id, [FromBody] ProviderDto dto)
    {
        var provider = await _agentDbContext.Providers.FindAsync(id);
        if (provider == null) return NotFound();
        provider.Name = dto.Name;
        provider.ProviderType = dto.ProviderType;
        provider.Endpoint = dto.Endpoint;
        provider.ApiKey = dto.ApiKey;
        provider.DefaultModel = dto.DefaultModel;
        provider.UpdatedAt = DateTime.UtcNow;
        await _agentDbContext.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Deletes a provider if no agents are using it.
    /// </summary>
    [HttpDelete("providers/{id}")]
    public async Task<IActionResult> DeleteProvider(Guid id)
    {
        var provider = await _agentDbContext.Providers
            .Include(p => p.Agents)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (provider == null) return NotFound();
        if (provider.Agents.Count > 0)
            return BadRequest($"Cannot delete provider '{provider.Name}': {provider.Agents.Count} agent(s) are using it.");
        _agentDbContext.Providers.Remove(provider);
        await _agentDbContext.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Tests connection to a provider by fetching models.
    /// </summary>
    [HttpPost("providers/{id}/test")]
    public async Task<IActionResult> TestProviderConnection(Guid id)
    {
        var provider = await _agentDbContext.Providers.FindAsync(id);
        if (provider == null) return NotFound();
        try
        {
            var models = await _modelDiscoveryService.GetModelsAsync(provider.ProviderType, provider.Endpoint, provider.ApiKey);
            return Ok(new TestConnectionResult { Success = true, ModelCount = models.Count, Models = models });
        }
        catch (Exception ex)
        {
            return Ok(new TestConnectionResult { Success = false, ErrorMessage = ex.Message });
        }
    }

    /// <summary>
    /// Fetches available models for a provider.
    /// </summary>
    [HttpGet("providers/{id}/models")]
    public async Task<IActionResult> GetProviderModels(Guid id)
    {
        var provider = await _agentDbContext.Providers.FindAsync(id);
        if (provider == null) return NotFound();
        try
        {
            var models = await _modelDiscoveryService.GetModelsAsync(provider.ProviderType, provider.Endpoint, provider.ApiKey);
            return Ok(models);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}