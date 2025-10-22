namespace NTG.Agent.Shared.Dtos.Tools;

public record ToolDto(Guid agentId, string Name, string Description, DateTime CreatedAt, DateTime UpdatedAt);
