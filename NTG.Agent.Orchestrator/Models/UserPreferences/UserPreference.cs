namespace NTG.Agent.Orchestrator.Models.UserPreferences;

/// <summary>
/// Represents user preferences for the application.
/// Supports both authenticated users (via UserId) and anonymous users (via SessionId).
/// </summary>
public class UserPreference
{
    public UserPreference()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }
    
    /// <summary>
    /// User ID for authenticated users. Null for anonymous users.
    /// </summary>
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Session ID for anonymous users. Null for authenticated users.
    /// </summary>
    public Guid? SessionId { get; set; }
    
    /// <summary>
    /// The selected agent ID for this user/session.
    /// </summary>
    public Guid SelectedAgentId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
