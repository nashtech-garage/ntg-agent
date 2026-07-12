namespace NTG.Agent.Orchestrator.Models.Settings;

/// <summary>
/// System-wide configuration for the lightweight model used to auto-generate conversation titles.
/// </summary>
/// <remarks>
/// Stored as a single row so an admin can point title generation at a small/cheap model,
/// independent of the Default Agent, and edit it from the admin panel. It is read once
/// (sequentially) at the start of a naming call; the title agent is then built from these plain
/// values and its LLM call runs with no further database access — which is what lets the naming run
/// concurrently with the chat stream without racing the request-scoped <c>AgentDbContext</c>.
/// </remarks>
public class TitleGenerationSettings
{
    /// <summary>Fixed identifier of the single settings row (seeded once).</summary>
    public static readonly Guid SingletonId = new("b5e7a3c1-9f2d-4e8a-bc10-000000000001");

    /// <summary>Primary key. Always <see cref="SingletonId"/> for the one settings row.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Provider key matching <c>AgentFactory</c>'s provider switch
    /// (e.g. "AzureOpenAI", "OpenAI", "GitHubModel", "GoogleGemini", "Anthropic").
    /// Empty means "not configured" — naming falls back to the Default Agent.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Deployment/model name of the title model (e.g. "gpt-5.4-mini").</summary>
    public string ProviderModelName { get; set; } = string.Empty;

    /// <summary>Provider endpoint URL. May be empty for providers that use their default endpoint.</summary>
    public string ProviderEndpoint { get; set; } = string.Empty;

    /// <summary>API key/token for the provider.</summary>
    public string ProviderApiKey { get; set; } = string.Empty;

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
