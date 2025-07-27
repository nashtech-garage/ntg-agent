namespace NTG.Agent.Shared.Dtos.Agents;

public record AgentListItem (Guid Id, string Name, string OwnerEmail, string UpdatedByEmail, DateTime UpdatedAt);
