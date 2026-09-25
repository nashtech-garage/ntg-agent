using NTG.Agent.Common.Dtos.UserPreferences;
using System.Net.Http.Json;

namespace NTG.Agent.WebClient.Client.Services;

public class PreferenceClient(HttpClient httpClient)
{
    /// <summary>
    /// Retrieves the user's preference settings from the server.
    /// </summary>
    /// <param name="currentSessionId">The session ID for anonymous users. Can be null for authenticated users.</param>
    /// <returns>The user's preference DTO, or null if no preferences are found.</returns>
    public async Task<UserPreferenceDto?> GetPreferenceAsync(string? currentSessionId)
    {
        string url = string.IsNullOrWhiteSpace(currentSessionId)
            ? "/api/preferences"
            : $"/api/preferences?currentSessionId={Uri.EscapeDataString(currentSessionId)}";

        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            // Return null if preference not found (404) or other errors
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<UserPreferenceDto>();
        return result;
    }

    /// <summary>
    /// Saves the user's preference settings to the server.
    /// </summary>
    /// <param name="selectedAgentId">The ID of the selected agent.</param>
    /// <param name="isLongTermMemoryEnabled">Optional: Enable or disable long-term memory feature.</param>
    /// <param name="isMemorySearchEnabled">Optional: Enable or disable memory search feature.</param>
    /// <param name="currentSessionId">The session ID for anonymous users. Can be null for authenticated users.</param>
    /// <param name="appearanceTheme">Optional: The preferred UI theme ("light" or "dark").</param>
    /// <param name="accentColor">Optional: The preferred accent color key.</param>
    /// <returns>True if the preference was saved successfully, false otherwise.</returns>
    public async Task<bool> SavePreferenceAsync(
        Guid selectedAgentId, 
        bool? isLongTermMemoryEnabled = null,
        bool? isMemorySearchEnabled = null,
        string? currentSessionId = null,
        string? appearanceTheme = null,
        string? accentColor = null)
    {
        string url = string.IsNullOrWhiteSpace(currentSessionId)
            ? "/api/preferences"
            : $"/api/preferences?currentSessionId={Uri.EscapeDataString(currentSessionId)}";

        var request = new SaveUserPreferenceRequest(
            selectedAgentId,
            isLongTermMemoryEnabled,
            isMemorySearchEnabled,
            appearanceTheme,
            accentColor);

        var response = await httpClient.PutAsJsonAsync(url, request);

        return response.IsSuccessStatusCode;
    }
}
