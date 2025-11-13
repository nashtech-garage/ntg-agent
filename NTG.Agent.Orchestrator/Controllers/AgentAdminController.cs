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

    /// <summary>
    /// Retrieves a list of all agents in the system.
    /// </summary>
    /// <remarks>
    /// This endpoint returns a summary list of all agents including their ID, name, owner, last updater, and last update timestamp.
    /// Only users with Admin role can access this endpoint.
    /// </remarks>
    /// <returns>
    /// An <see cref="IActionResult"/> containing a list of <see cref="AgentListItem"/> objects.
    /// Returns 200 OK with the agent list.
    /// </returns>
    /// <response code="200">Returns the list of agents successfully</response>
    /// <response code="401">If the user is not authenticated</response>
    /// <response code="403">If the user does not have Admin role</response>
    [HttpGet]
    public async Task<IActionResult> GetAgents()
    {
        var agents = await _agentDbContext.Agents
            .Select(x => new AgentListItem(x.Id, x.Name, x.OwnerUser.Email, x.UpdatedByUser.Email, x.UpdatedAt))
            .ToListAsync();
        return Ok(agents);
    }

    /// <summary>
    /// Retrieves detailed information about a specific agent by its unique identifier.
    /// </summary>
    /// <remarks>
    /// This endpoint returns comprehensive details about an agent including its configuration, 
    /// provider settings, and tool count. Only users with Admin role can access this endpoint.
    /// </remarks>
    /// <param name="id">The unique identifier (GUID) of the agent to retrieve.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> containing an <see cref="AgentDetail"/> object.
    /// Returns 200 OK with the agent details if found, or 404 Not Found if the agent doesn't exist.
    /// </returns>
    /// <response code="200">Returns the agent details successfully</response>
    /// <response code="404">If the agent with the specified ID is not found</response>
    /// <response code="401">If the user is not authenticated</response>
    /// <response code="403">If the user does not have Admin role</response>
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

    /// <summary>
    /// Updates an existing agent's configuration and settings.
    /// </summary>
    /// <remarks>
    /// This endpoint allows modification of an agent's properties including name, instructions, 
    /// provider configuration, and MCP server settings. The authenticated user is recorded as 
    /// the updater, and the update timestamp is automatically set to the current UTC time.
    /// Only users with Admin role can access this endpoint.
    /// </remarks>
    /// <param name="id">The unique identifier (GUID) of the agent to update.</param>
    /// <param name="updatedAgent">An <see cref="AgentDetail"/> object containing the updated agent information.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> indicating the result of the update operation.
    /// Returns 204 No Content on success, 400 Bad Request if IDs don't match, or 404 Not Found if the agent doesn't exist.
    /// </returns>
    /// <response code="204">Agent updated successfully</response>
    /// <response code="400">If the ID in the URL doesn't match the ID in the request body</response>
    /// <response code="404">If the agent with the specified ID is not found</response>
    /// <response code="401">If the user is not authenticated</response>
    /// <response code="403">If the user does not have Admin role</response>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
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
        agent.Instructions = updatedAgent.Instructions ?? string.Empty;
        agent.ProviderName = updatedAgent.ProviderName ?? string.Empty;
        agent.ProviderEndpoint = updatedAgent.ProviderEndpoint ?? string.Empty;
        agent.ProviderApiKey = updatedAgent.ProviderApiKey ?? string.Empty;
        agent.ProviderModelName = updatedAgent.ProviderModelName ?? string.Empty;
        agent.McpServer = updatedAgent.McpServer;
        agent.UpdatedAt = DateTime.UtcNow;
        agent.UpdatedByUserId = userId;
        await _agentDbContext.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Retrieves all available tools for a specific agent, including their enabled status.
    /// </summary>
    /// <remarks>
    /// This endpoint returns a merged list of all available tools (built-in, MCP, and custom) 
    /// with their current configuration status for the specified agent. It combines tools from 
    /// the agent factory with the agent's saved tool preferences.
    /// Only users with Admin role can access this endpoint.
    /// </remarks>
    /// <param name="id">The unique identifier (GUID) of the agent whose tools to retrieve.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> containing a list of <see cref="AgentToolDto"/> objects.
    /// Returns 200 OK with the tool list, or 404 Not Found if the tools are not available.
    /// </returns>
    /// <response code="200">Returns the list of agent tools successfully</response>
    /// <response code="404">If the tools list is not available or agent is not found</response>
    /// <response code="401">If the user is not authenticated</response>
    /// <response code="403">If the user does not have Admin role</response>
    /// <exception cref="ArgumentException">Thrown if the agent with the specified ID is not found.</exception>
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
    /// <remarks>
    /// This endpoint allows enabling/disabling tools for an agent. For existing tools in the database, 
    /// it updates their enabled status. For new tools, it creates new tool associations with the agent.
    /// The update timestamp is automatically set for modified tools.
    /// Only users with Admin role can access this endpoint.
    /// </remarks>
    /// <param name="id">The unique identifier (GUID) of the agent whose tools to update.</param>
    /// <param name="updatedTools">A list of <see cref="AgentToolDto"/> objects representing the updated tool configurations.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> indicating the result of the update operation.
    /// Returns 200 OK with a success message, or 404 Not Found if the agent doesn't exist.
    /// </returns>
    /// <response code="200">Tools updated successfully</response>
    /// <response code="404">If the agent with the specified ID is not found</response>
    /// <response code="401">If the user is not authenticated</response>
    /// <response code="403">If the user does not have Admin role</response>
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


    /// <summary>
    /// Connects an agent to a Model Context Protocol (MCP) server and retrieves available tools.
    /// </summary>
    /// <remarks>
    /// This endpoint establishes a connection to an MCP server using the provided endpoint URL 
    /// and retrieves all available tools from that server. The tools are then merged with the 
    /// agent's existing tool configuration to show which tools are enabled.
    /// Only users with Admin role can access this endpoint.
    /// </remarks>
    /// <param name="id">The unique identifier (GUID) of the agent to connect to the MCP server.</param>
    /// <param name="endpoint">The MCP server endpoint URL to connect to.</param>
    /// <returns>
    /// An <see cref="IEnumerable{AgentToolDto}"/> containing the list of available tools from the MCP server 
    /// merged with the agent's current tool configuration.
    /// </returns>
    /// <response code="200">Returns the list of MCP tools successfully</response>
    /// <response code="401">If the user is not authenticated</response>
    /// <response code="403">If the user does not have Admin role</response>
    /// <exception cref="ArgumentException">Thrown if the agent with the specified ID is not found.</exception>
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
    /// <remarks>
    /// This endpoint creates a new agent in the system with the provided configuration including 
    /// name, instructions, provider settings, and MCP server configuration. The authenticated user 
    /// is set as both the owner and updater of the agent. All optional fields (instructions, provider 
    /// settings) default to empty strings if not provided. A new GUID is automatically generated for 
    /// the agent, and creation/update timestamps are set to the current UTC time.
    /// Only users with Admin role can access this endpoint.
    /// 
    /// After successful creation, the agent's ID should be used to create a default folder structure 
    /// via the FoldersController endpoint: POST /api/folders/{agentId}/folders/default
    /// </remarks>
    /// <param name="updatedAgent">An <see cref="AgentDetail"/> object containing the new agent's configuration. 
    /// Only the Name field is required; all other fields are optional.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> containing the created agent.
    /// Returns 201 Created with the agent details and a Location header pointing to the GetAgentById endpoint,
    /// or 400 Bad Request if the agent data is invalid.
    /// </returns>
    /// <response code="201">Agent created successfully, returns the created agent with its ID</response>
    /// <response code="400">If the agent data is null or invalid</response>
    /// <response code="401">If the user is not authenticated</response>
    /// <response code="403">If the user does not have Admin role</response>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
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
            Instructions = updatedAgent.Instructions ?? string.Empty,
            ProviderName = updatedAgent.ProviderName ?? string.Empty,
            McpServer = updatedAgent.McpServer,
            ProviderEndpoint = updatedAgent.ProviderEndpoint ?? string.Empty,
            ProviderApiKey = updatedAgent.ProviderApiKey ?? string.Empty,
            ProviderModelName = updatedAgent.ProviderModelName ?? string.Empty,
            UpdatedByUserId = userId,
            OwnerUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _agentDbContext.Agents.Add(agent);
        await _agentDbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAgentById), new { id = agent.Id }, agent);
    }
}
