using System.ComponentModel.DataAnnotations;

namespace NTG.Agent.Shared.Dtos.UserPreferences;

/// <summary>
/// Request to save user preference.
/// </summary>
public record SaveUserPreferenceRequest(
    [Required]
    Guid SelectedAgentId
);
