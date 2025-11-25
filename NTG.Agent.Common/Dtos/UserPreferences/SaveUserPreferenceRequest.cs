using System.ComponentModel.DataAnnotations;
namespace NTG.Agent.Common.Dtos.UserPreferences;
public record SaveUserPreferenceRequest([Required] Guid SelectedAgentId);
