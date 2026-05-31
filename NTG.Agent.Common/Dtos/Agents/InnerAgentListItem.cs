namespace NTG.Agent.Common.Dtos.Agents;

public record InnerAgentListItem(
    Guid Id,
    string Name,
    string? Description,
    string ProviderName,
    string ProviderModelName,
    DateTime UpdatedAt);
