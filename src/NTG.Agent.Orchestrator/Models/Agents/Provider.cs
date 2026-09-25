using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Models.Agents;

public class Provider
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>Azure AI Foundry account name for the deployments discovery endpoint (AzureOpenAI only).</summary>
    public string? AzureAiAccountName { get; set; }

    /// <summary>Azure AI Foundry project name for the deployments discovery endpoint (AzureOpenAI only).</summary>
    public string? AzureAiProjectName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();

    /// <summary>Models the admin has enabled for this provider, each with its thinking-mode availability.</summary>
    public ICollection<ProviderModel> Models { get; set; } = new List<ProviderModel>();
}
