namespace NTG.Agent.Orchestrator.Models.Agents;

/// <summary>
/// A model enabled on a provider. The admin decides which discovered models agents may use
/// and whether Thinking mode is available for each.
/// </summary>
public class ProviderModel
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public Provider Provider { get; set; } = null!;

    /// <summary>The model id (or Azure deployment name) sent to the provider API.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Human-friendly name when the provider offers one (e.g., Anthropic, Azure deployments).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Whether the admin allows Thinking mode for this model.</summary>
    public bool AllowsThinking { get; set; }
}
