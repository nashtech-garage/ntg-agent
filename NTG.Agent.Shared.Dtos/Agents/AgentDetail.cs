namespace NTG.Agent.Shared.Dtos.Agents;

public class AgentDetail
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Instructions { get; set; }
    public string ProviderName { get; set; }
    public string ProviderEndpoint { get; set; }
    public string ProviderApiKey { get; set; }
    public string ProviderModelName { get; set; }

    public string? McpServer { get; set; }

    public string ToolCount { get; set; } = "0";

    public AgentDetail(
        Guid id,
        string name,
        string instructions,
        string providerName,
        string providerEndpoint,
        string providerApiKey,
        string providerModelName)
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
