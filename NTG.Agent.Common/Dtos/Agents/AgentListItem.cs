namespace NTG.Agent.Common.Dtos.Agents;

public record AgentListItem (Guid Id, string Name, string OwnerEmail, string UpdatedByEmail, DateTime UpdatedAt, bool IsDefault, bool IsPublished, bool IsSelectable);
public record AgentListItemDto(Guid Id, string Name, bool IsDefault, AgentMode Mode = AgentMode.Fast);

