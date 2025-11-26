using System.ComponentModel.DataAnnotations;

namespace NTG.Agent.Common.Dtos.Agents;

public class AgentDetail
{
    public Guid Id { get; set; }
    
    [Required(ErrorMessage = "Agent Name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Agent Name must be between 1 and 200 characters")]
    public string Name { get; set; }

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }
    
    public string? Instructions { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderEndpoint { get; set; }
    public string? ProviderApiKey { get; set; }
    public string? ProviderModelName { get; set; }
    public bool IsDefault { get; set; }
    public bool IsPublished { get; set; }
    public string? McpServer { get; set; }

    public string ToolCount { get; set; } = "0";

    public AgentDetail()
    {
        Name = string.Empty;
    }

    public AgentDetail(
        Guid id,
        string name,
        string? instructions,
        string? providerName,
        string? providerEndpoint,
        string? providerApiKey,
        string? providerModelName)
    {
        Id = id;
        Name = name;
        Instructions = instructions;
        ProviderName = providerName;
        ProviderEndpoint = providerEndpoint;
        ProviderApiKey = providerApiKey;
        ProviderModelName = providerModelName;
    }
}
