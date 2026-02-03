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
        IsLongTermMemoryEnabled = null;
        IsMemorySearchEnabled = null;
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
    
    /// <summary>
    /// Indicates whether Long-Term Memory feature is enabled for this user/session.
    /// When false, no memories will be extracted or stored for this user.
    /// Default: null
    /// </summary>
    public bool? IsLongTermMemoryEnabled { get; set; }
    
    /// <summary>
    /// Indicates whether Memory Search feature is enabled for this user/session.
    /// When false, no memories will be retrieved and injected into chat context.
    /// Note: This only affects memory retrieval, not memory storage.
    /// Default: null
    /// </summary>
    public bool? IsMemorySearchEnabled { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
