using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Knowledge;
using NTG.Agent.Orchestrator.Services;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Common.Dtos.Tags;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AgentAdminController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    private readonly IAgentFactory _agentFactory;
    private readonly IKnowledgeProvisioner _knowledgeProvisioner;
    private readonly IKnowledgeService _knowledgeService;
    private readonly AgentProvisioningSignal _provisioningSignal;
    private readonly ILogger<AgentAdminController> _logger;
    private readonly AgentAccessService _agentAccessService;
    private readonly ModelDiscoveryService _modelDiscoveryService;
    private readonly IThinkingSupportProbe _thinkingSupportProbe;

    public AgentAdminController(AgentDbContext agentDbContext,
        IAgentFactory agentFactory,
        IKnowledgeProvisioner knowledgeProvisioner,
        IKnowledgeService knowledgeService,
        AgentProvisioningSignal provisioningSignal,
        ILogger<AgentAdminController> logger,
        AgentAccessService agentAccessService,
        ModelDiscoveryService modelDiscoveryService,
        IThinkingSupportProbe thinkingSupportProbe
        )
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _knowledgeProvisioner = knowledgeProvisioner ?? throw new ArgumentNullException(nameof(knowledgeProvisioner));
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _provisioningSignal = provisioningSignal ?? throw new ArgumentNullException(nameof(provisioningSignal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentAccessService = agentAccessService ?? throw new ArgumentNullException(nameof(agentAccessService));
        _modelDiscoveryService = modelDiscoveryService ?? throw new ArgumentNullException(nameof(modelDiscoveryService));
        _thinkingSupportProbe = thinkingSupportProbe ?? throw new ArgumentNullException(nameof(thinkingSupportProbe));
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
            .Select(x => new AgentListItem(x.Id, x.Name, x.OwnerUser.Email, x.UpdatedByUser.Email, x.UpdatedAt, x.IsDefault, x.IsPublished, x.AgentKind, x.ProvisioningStatus, x.ProvisioningError, x.Provider != null ? x.Provider.Name : null))
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
                Mode = x.Mode,
                Temperature = x.Temperature,
                MaxOutputTokens = x.MaxOutputTokens,
                ProvisioningStatus = x.ProvisioningStatus,
                ProvisioningError = x.ProvisioningError
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
        agent.Temperature = updatedAgent.Temperature;
        agent.MaxOutputTokens = updatedAgent.MaxOutputTokens;
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
    public async Task<IActionResult> CreateAgent([FromBody] AgentDetail updatedAgent, CancellationToken cancellationToken = default)
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
            Temperature = updatedAgent.Temperature,
            MaxOutputTokens = updatedAgent.MaxOutputTokens,
            UpdatedByUserId = userId,
            OwnerUserId = userId,
            IsDefault = false,
            IsPublished = false,
            AgentKind = updatedAgent.AgentKind,
            ProvisioningStatus = AgentProvisioningStatus.Provisioning,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _agentDbContext.Agents.Add(agent);
        await _agentDbContext.SaveChangesAsync(cancellationToken);

        // Provisioning its lightRAG container can take seconds, so
        // it runs in the background: the agent is persisted as Provisioning and the worker boots the
        // container, then flips it to Ready/Failed. The create response returns immediately (202) with trackable status.
        _provisioningSignal.Notify();
        _logger.LogInformation("Agent {AgentId} created; queued for background provisioning.", agent.Id);

        return AcceptedAtAction(nameof(GetAgentById), new { id = agent.Id }, agent.Id);
    }

    /// <summary>
    /// Re-runs the provisioning process for an agent when it has failed.
    /// </summary>
    [HttpPost("{id}/reprovision")]
    public async Task<IActionResult> ReprovisionAgent(Guid id, CancellationToken cancellationToken = default)
    {
        var agent = await _agentDbContext.Agents.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent == null)
        {
            return NotFound($"Agent with ID '{id}' not found.");
        }

        agent.ProvisioningStatus = AgentProvisioningStatus.Provisioning;
        agent.ProvisioningError = null;
        agent.ProvisionedAt = null;
        await _agentDbContext.SaveChangesAsync(cancellationToken);

        _provisioningSignal.Notify();
        _logger.LogInformation("Agent {AgentId} queued for re-provisioning.", agent.Id);

        return Accepted();
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

        // Only allow fully-provisioned agents to be published to the end users via api/agents
        if (isPublished && agent.ProvisioningStatus != AgentProvisioningStatus.Ready)
        {
            return Conflict($"Agent cannot be published while its provisioning status is '{agent.ProvisioningStatus}'. It must be Ready.");
        }

        agent.IsPublished = isPublished;
        agent.UpdatedAt = DateTime.UtcNow;
        agent.UpdatedByUserId = userId;
        
        await _agentDbContext.SaveChangesAsync();

        return Ok(new { message = $"Agent successfully {(isPublished ? "published" : "unpublished")}.", isPublished = agent.IsPublished });
    }

    /// <summary>
    /// Lists all role grants for a specific agent.
    /// </summary>
    [HttpGet("{agentId}/access")]
    public async Task<IActionResult> GetAgentAccess(Guid agentId)
    {
        var agent = await _agentDbContext.Agents.FindAsync(agentId);
        if (agent == null) return NotFound($"Agent with ID '{agentId}' not found.");

        var grants = await _agentDbContext.AgentRoles
            .Where(ar => ar.AgentId == agentId)
            .Join(_agentDbContext.Roles,
                ar => ar.RoleId,
                r => r.Id,
                (ar, r) => new AgentRoleGrantDto(ar.RoleId, r.Name, ar.CreatedAt))
            .ToListAsync();

        return Ok(grants);
    }

    /// <summary>
    /// Grants a role access to an agent. Idempotent: returns 200 with existing grant if already exists.
    /// </summary>
    [HttpPost("{agentId}/access")]
    public async Task<IActionResult> GrantAgentAccess(Guid agentId, [FromBody] GrantAgentAccessRequest request)
    {
        var agent = await _agentDbContext.Agents.FindAsync(agentId);
        if (agent == null) return NotFound($"Agent with ID '{agentId}' not found.");

        var roleExists = await _agentDbContext.Roles.AnyAsync(r => r.Id == request.RoleId);
        if (!roleExists) return BadRequest($"Role with ID '{request.RoleId}' not found.");

        var existingGrant = await _agentDbContext.AgentRoles
            .FirstOrDefaultAsync(ar => ar.AgentId == agentId && ar.RoleId == request.RoleId);

        if (existingGrant != null)
        {
            var existingRole = await _agentDbContext.Roles.FirstAsync(r => r.Id == request.RoleId);
            return Ok(new AgentRoleGrantDto(existingGrant.RoleId, existingRole.Name, existingGrant.CreatedAt));
        }

        var grant = new AgentRole
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            RoleId = request.RoleId
        };

        _agentDbContext.AgentRoles.Add(grant);

        try
        {
            await _agentDbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race condition: another request inserted the same row
            var existing = await _agentDbContext.AgentRoles
                .FirstOrDefaultAsync(ar => ar.AgentId == agentId && ar.RoleId == request.RoleId);
            var existingRole = await _agentDbContext.Roles.FirstAsync(r => r.Id == request.RoleId);
            return Ok(new AgentRoleGrantDto(existing!.RoleId, existingRole.Name, existing!.CreatedAt));
        }

        var role = await _agentDbContext.Roles.FirstAsync(r => r.Id == request.RoleId);
        return Ok(new AgentRoleGrantDto(grant.RoleId, role.Name, grant.CreatedAt));
    }

    /// <summary>
    /// Revokes a role's access to an agent. Idempotent: returns 204 whether or not the grant existed.
    /// </summary>
    [HttpDelete("{agentId}/access/{roleId}")]
    public async Task<IActionResult> RevokeAgentAccess(Guid agentId, Guid roleId)
    {
        var agent = await _agentDbContext.Agents.FindAsync(agentId);
        if (agent == null) return NotFound($"Agent with ID '{agentId}' not found.");

        var grant = await _agentDbContext.AgentRoles
            .FirstOrDefaultAsync(ar => ar.AgentId == agentId && ar.RoleId == roleId);

        if (grant != null)
        {
            _agentDbContext.AgentRoles.Remove(grant);
            await _agentDbContext.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>
    /// Lists all available roles for access management.
    /// </summary>
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _agentDbContext.Roles
            .Select(r => new RoleDto(r.Id, r.Name))
            .ToListAsync();
        return Ok(roles);
    }


    /// <summary>
    /// Deletes the agent with the specified identifier.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(Guid id, CancellationToken cancellationToken = default)
    {
        var agent = await _agentDbContext.Agents.FindAsync([id], cancellationToken);
        if (agent == null)
        {
            return NotFound();
        }

        if (agent.IsDefault)
        {
            return BadRequest("Default agent cannot be deleted.");
        }

        // If this agent is linked as an inner agent of any outer agent, remove those
        // bindings first — the InnerAgentId FK is Restrict (no cascade), so the delete
        // would otherwise fail. OuterAgent bindings cascade automatically.
        var innerBindings = await _agentDbContext.AgentInnerAgents
            .Where(b => b.InnerAgentId == id)
            .ToListAsync(cancellationToken);
        if (innerBindings.Count > 0)
        {
            _agentDbContext.AgentInnerAgents.RemoveRange(innerBindings);
        }

        // Cascade-delete the agent's knowledge: remove each document from the knowledge
        // backend while it is still alive, then drop the SQL rows, then tear down the
        // agent's backend resources. Document removal is best-effort so a single backend
        // hiccup can't block deleting the agent.
        var documents = await _agentDbContext.Documents.Where(d => d.AgentId == id).ToListAsync(cancellationToken);
        foreach (var doc in documents)
        {
            try
            {
                await _knowledgeService.RemoveDocumentAsync(id, doc.Id, doc.KnowledgeDocId, doc.TrackId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove document {DocId} from the knowledge backend while deleting agent {AgentId}; continuing.", doc.Id, id);
            }
        }
        _agentDbContext.Documents.RemoveRange(documents);

        await _knowledgeProvisioner.DeprovisionAgentAsync(id, cancellationToken);

        _agentDbContext.Agents.Remove(agent);
        await _agentDbContext.SaveChangesAsync(cancellationToken);

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
                ModelOverride = a.ModelOverride
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
                p.AzureAiAccountName,
                p.AzureAiProjectName,
                AgentCount = p.Agents.Count,
                p.CreatedAt,
                p.UpdatedAt,
                Models = p.Models.OrderBy(m => m.ModelId).Select(m => new ProviderModelDto
                {
                    Id = m.Id,
                    ModelId = m.ModelId,
                    DisplayName = m.DisplayName,
                    AllowsThinking = m.AllowsThinking
                }).ToList()
            })
            .ToListAsync();
        var result = providers.Select(p => new ProviderDto
        {
            Id = p.Id,
            Name = p.Name,
            ProviderType = p.ProviderType,
            Endpoint = p.Endpoint,
            // Never return the API key (even masked) — read endpoints only report
            // whether one is configured so secrets don't leak into browser state/logs.
            HasApiKey = !string.IsNullOrEmpty(p.ApiKey),
            AzureAiAccountName = p.AzureAiAccountName,
            AzureAiProjectName = p.AzureAiProjectName,
            Models = p.Models,
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
                // Never echo the stored key back to the client.
                ApiKey = null,
                HasApiKey = !string.IsNullOrEmpty(p.ApiKey),
                AzureAiAccountName = p.AzureAiAccountName,
                AzureAiProjectName = p.AzureAiProjectName,
                Models = p.Models.OrderBy(m => m.ModelId).Select(m => new ProviderModelDto
                {
                    Id = m.Id,
                    ModelId = m.ModelId,
                    DisplayName = m.DisplayName,
                    AllowsThinking = m.AllowsThinking
                }).ToList(),
                AgentCount = p.Agents.Count,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .FirstOrDefaultAsync(p => p.Id == id);

        if (provider == null) return NotFound();
        return Ok(provider);
    }

    /// <summary>
    /// Creates a new provider, including the models the admin enabled for it.
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
            AzureAiAccountName = dto.AzureAiAccountName,
            AzureAiProjectName = dto.AzureAiProjectName,
            Models = (dto.Models ?? []).Select(m => new Models.Agents.ProviderModel
            {
                Id = Guid.NewGuid(),
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                AllowsThinking = m.AllowsThinking
            }).ToList(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _agentDbContext.Providers.Add(provider);
        await _agentDbContext.SaveChangesAsync();
        return CreatedAtAction(nameof(GetProvider), new { id = provider.Id }, provider.Id);
    }

    /// <summary>
    /// Updates an existing provider, replacing its enabled-model list wholesale.
    /// </summary>
    [HttpPut("providers/{id}")]
    public async Task<IActionResult> UpdateProvider(Guid id, [FromBody] ProviderDto dto)
    {
        var provider = await _agentDbContext.Providers
            .Include(p => p.Models)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (provider == null) return NotFound();
        provider.Name = dto.Name;
        provider.ProviderType = dto.ProviderType;
        provider.Endpoint = dto.Endpoint;
        // Only replace the stored key when the client sends a new one — the read endpoints
        // never echo the key back, so a blank value means "keep the existing key", not "clear it".
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            provider.ApiKey = dto.ApiKey;
        provider.AzureAiAccountName = dto.AzureAiAccountName;
        provider.AzureAiProjectName = dto.AzureAiProjectName;
        provider.UpdatedAt = DateTime.UtcNow;

        // Replace the enabled-model list. ProviderModel.Id is a client-generated Guid, so new
        // instances must be attached explicitly as Added — letting DetectChanges discover them
        // through the navigation marks them Modified (no store-generated key) and EF then emits
        // UPDATEs against rows that don't exist, surfacing as DbUpdateConcurrencyException.
        var oldModels = provider.Models.ToList();
        _agentDbContext.RemoveRange(oldModels);
        provider.Models.Clear();

        var newModels = (dto.Models ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.ModelId))
            .Select(m => new Models.Agents.ProviderModel
            {
                Id = Guid.NewGuid(),
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                AllowsThinking = m.AllowsThinking
            })
            .ToList();
        foreach (var model in newModels)
        {
            provider.Models.Add(model);
        }
        _agentDbContext.AddRange(newModels);

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
            var models = await _modelDiscoveryService.GetModelsAsync(provider.ProviderType, provider.Endpoint, provider.ApiKey, provider.AzureAiAccountName, provider.AzureAiProjectName);
            return Ok(new TestConnectionResult { Success = true, ModelCount = models.Count, Models = models });
        }
        catch (Exception ex)
        {
            // Broad by design: any discovery failure is surfaced to the admin as the
            // connection-test verdict; logged here so server-side diagnostics keep the detail.
            _logger.LogError(ex, "Provider connection test failed for provider {ProviderId}", id);
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
            var models = await _modelDiscoveryService.GetModelsAsync(provider.ProviderType, provider.Endpoint, provider.ApiKey, provider.AzureAiAccountName, provider.AzureAiProjectName);
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model discovery failed for provider {ProviderId}", id);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Live-checks whether one of a provider's models supports thinking/reasoning mode by sending
    /// a small test request with the thinking parameter enabled, using the same payload shape as
    /// the chat path's Thinking mode (see <see cref="ThinkingSupportProbe"/>). Replaces the old
    /// curated model-name list: capability is verified against the provider itself.
    /// </summary>
    [HttpPost("providers/{id}/models/thinking-support")]
    public async Task<IActionResult> CheckThinkingSupport(Guid id, [FromBody] ThinkingSupportRequest request)
    {
        var provider = await _agentDbContext.Providers.FindAsync(id);
        if (provider == null) return NotFound();
        return Ok(await _thinkingSupportProbe.ProbeAsync(provider, request.ModelId));
    }
}