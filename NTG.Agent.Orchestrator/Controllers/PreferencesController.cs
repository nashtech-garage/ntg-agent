using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.UserPreferences;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.UserPreferences;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PreferencesController : ControllerBase
{
    private readonly AgentDbContext _context;
    private readonly ILogger<PreferencesController> _logger;

    public PreferencesController(AgentDbContext context, ILogger<PreferencesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the user's preference settings.
    /// </summary>
    /// <remarks>
    /// This method supports both authenticated and unauthenticated users. 
    /// Authenticated users are identified by their user ID, while unauthenticated users must provide a valid session ID.
    /// </remarks>
    /// <param name="currentSessionId">The session identifier for the current user session. Required for unauthenticated requests.</param>
    /// <returns>
    /// An <see cref="ActionResult{T}"/> containing the <see cref="UserPreferenceDto"/> if found; 
    /// otherwise, a <see cref="NotFoundResult"/> if no preferences exist for the user.
    /// </returns>
    [HttpGet]
    public async Task<ActionResult<UserPreferenceDto>> GetPreference([FromQuery] string? currentSessionId)
    {
        Guid? userId = User.GetUserId();
        UserPreference? preference;

        if (userId.HasValue)
        {
            // Authenticated user
            preference = await _context.UserPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId.Value);
        }
        else
        {
            // Anonymous user: sessionId must be provided and valid
            if (string.IsNullOrWhiteSpace(currentSessionId) || !Guid.TryParse(currentSessionId, out Guid sessionId))
            {
                return BadRequest("A valid Session ID is required for unauthenticated requests.");
            }

            preference = await _context.UserPreferences
                .FirstOrDefaultAsync(p => p.SessionId == sessionId);
        }

        if (preference is null)
        {
            return NotFound();
        }

        return Ok(new UserPreferenceDto(
            preference.SelectedAgentId,
            preference.IsLongTermMemoryEnabled,
            preference.IsMemorySearchEnabled
        ));
    }

    /// <summary>
    /// Saves or updates the user's preference settings.
    /// </summary>
    /// <remarks>
    /// This method supports both authenticated and unauthenticated users.
    /// If a preference already exists for the user/session, it will be updated; otherwise, a new preference will be created.
    /// </remarks>
    /// <param name="request">The preference data to save.</param>
    /// <param name="currentSessionId">The session identifier for the current user session. Required for unauthenticated requests.</param>
    /// <returns>
    /// An <see cref="ActionResult{T}"/> containing the saved <see cref="UserPreferenceDto"/>.
    /// </returns>
    [HttpPut]
    public async Task<ActionResult<UserPreferenceDto>> SavePreference(
        [FromBody] SaveUserPreferenceRequest request,
        [FromQuery] string? currentSessionId)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        Guid? userId = User.GetUserId();
        UserPreference? preference;

        if (userId.HasValue)
        {
            // Authenticated user
            preference = await _context.UserPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId.Value);

            if (preference is null)
            {
                // Create new preference for authenticated user
                // Leave memory preferences as NULL initially (default enabled behavior)
                // They will only be set when user explicitly changes them in My Preferences
                preference = new UserPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    SelectedAgentId = request.SelectedAgentId,
                    IsLongTermMemoryEnabled = request.IsLongTermMemoryEnabled,
                    IsMemorySearchEnabled = request.IsMemorySearchEnabled
                };
                _context.UserPreferences.Add(preference);
            }
            else
            {
                // Update existing preference
                preference.SelectedAgentId = request.SelectedAgentId;
                
                // Update memory preference if provided
                if (request.IsLongTermMemoryEnabled.HasValue)
                {
                    preference.IsLongTermMemoryEnabled = request.IsLongTermMemoryEnabled.Value;
                }
                
                // Update memory search preference if provided
                if (request.IsMemorySearchEnabled.HasValue)
                {
                    preference.IsMemorySearchEnabled = request.IsMemorySearchEnabled.Value;
                }
                
                preference.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            // Anonymous user: sessionId must be provided and valid
            if (string.IsNullOrWhiteSpace(currentSessionId) || !Guid.TryParse(currentSessionId, out Guid sessionId))
            {
                return BadRequest("A valid Session ID is required for unauthenticated requests.");
            }

            preference = await _context.UserPreferences
                .FirstOrDefaultAsync(p => p.SessionId == sessionId);

            if (preference is null)
            {
                preference = new UserPreference
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    SelectedAgentId = request.SelectedAgentId
                };
                _context.UserPreferences.Add(preference);
            }
            else
            {
                preference.SelectedAgentId = request.SelectedAgentId;
                preference.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new UserPreferenceDto(
            preference.SelectedAgentId,
            preference.IsLongTermMemoryEnabled,
            preference.IsMemorySearchEnabled
        ));
    }
}
