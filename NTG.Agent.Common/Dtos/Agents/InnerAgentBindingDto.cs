namespace NTG.Agent.Common.Dtos.Agents;

public class InnerAgentBindingDto
{
    public Guid InnerAgentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public string? ModelOverride { get; set; }
    public bool IsEnabled { get; set; }
}
