namespace NTG.Agent.Common.Dtos.Agents;

public record AgentListItem (Guid Id, string Name, string OwnerEmail, string UpdatedByEmail, DateTime UpdatedAt, bool IsDefault, bool IsPublished);
