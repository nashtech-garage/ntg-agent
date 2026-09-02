namespace NTG.Agent.Common.Dtos.Agents;

public class ProviderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>
    /// Whether the stored provider has an API key configured. Read endpoints never return
    /// the key itself (even masked) — this flag lets the UI indicate configuration state
    /// without exposing the secret.
    /// </summary>
    public bool HasApiKey { get; set; }

    /// <summary>
    /// Azure AI Foundry account name used by the deployments discovery endpoint
    /// (https://{account}.services.ai.azure.com/api/projects/{project}/deployments).
    /// Only relevant for <see cref="ProviderType.AzureOpenAI"/>.
    /// </summary>
    public string? AzureAiAccountName { get; set; }

    /// <summary>
    /// Azure AI Foundry project name used by the deployments discovery endpoint.
    /// Only relevant for <see cref="ProviderType.AzureOpenAI"/>.
    /// </summary>
    public string? AzureAiProjectName { get; set; }

    /// <summary>Models the admin has enabled for agents to use, with their thinking-mode availability.</summary>
    public List<ProviderModelDto> Models { get; set; } = new();

    public int AgentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
