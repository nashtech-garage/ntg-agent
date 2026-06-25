namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// Request to link a child (document) agent as a tool on a parent agent.
/// </summary>
public record AddAgentToolRequest(Guid LinkedAgentId, string? Name = null, string? Description = null);