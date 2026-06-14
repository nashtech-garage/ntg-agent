namespace NTG.Agent.Common.Dtos.Agents;

public record AgentRoleGrantDto(Guid RoleId, string RoleName, DateTime GrantedAt);
public record GrantAgentAccessRequest(Guid RoleId);
