namespace NTG.Agent.Shared.Dtos.Tags;

public record TagDto(Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt, int DocumentCount = 0);

public record TagCreateDto(string Name);

public record TagUpdateDto(string Name);

public record TagRoleDto(Guid Id, Guid TagId, Guid RoleId, DateTime CreatedAt, DateTime UpdatedAt);

public record TagRoleAttachDto(Guid RoleId);

public record RoleDto(Guid Id, string Name);
