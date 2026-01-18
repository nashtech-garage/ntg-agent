namespace NTG.Agent.Common.Dtos.UserPreferences;

public record UserPreferenceDto(
    Guid SelectedAgentId,
    bool? IsLongTermMemoryEnabled,
    bool? IsMemorySearchEnabled
);
