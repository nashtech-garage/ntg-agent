using System.ComponentModel.DataAnnotations;

namespace NTG.Agent.Common.Dtos.Agents;

public class InnerAgentDetail
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Agent Name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Agent Name must be between 1 and 200 characters")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }

    public string? Instructions { get; set; }

    public string? ProviderName { get; set; }

    public string? ProviderEndpoint { get; set; }

    public string? ProviderApiKey { get; set; }

    public string? ProviderModelName { get; set; }

    public string? McpServer { get; set; }
}
