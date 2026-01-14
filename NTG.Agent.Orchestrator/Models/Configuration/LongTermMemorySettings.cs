namespace NTG.Agent.Orchestrator.Models.Configuration;

/// <summary>
/// Configuration settings for the Long Term Memory feature.
/// </summary>
public class LongTermMemorySettings
{
    /// <summary>
    /// Gets or sets a value indicating whether Long Term Memory feature is enabled.
    /// When enabled, the system will extract and store user-specific information from conversations
    /// and retrieve relevant memories to provide personalized responses.
    /// Note: Enabling this feature increases token consumption as it requires additional LLM calls
    /// for memory extraction and adds context to each chat request.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the minimum confidence threshold (0.0 to 1.0) for storing extracted memories.
    /// Memories with confidence scores below this threshold will not be stored.
    /// Default: 0.3
    /// </summary>
    public float MinimumConfidenceThreshold { get; set; } = 0.3f;

    /// <summary>
    /// Gets or sets the maximum number of memories to retrieve for context injection.
    /// Higher values provide more context but consume more tokens.
    /// Default: 20
    /// </summary>
    public int MaxMemoriesToRetrieve { get; set; } = 20;
}
