namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// Minimal DTO for agents that can be linked as tools.
/// </summary>
public record LinkableAgentDto(Guid Id, string Name, string? Description);