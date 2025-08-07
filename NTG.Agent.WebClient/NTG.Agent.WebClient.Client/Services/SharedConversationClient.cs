using NTG.Agent.Shared.Dtos.SharedConversations;
using System.Net.Http.Json;

public class SharedConversationClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    // ✅ Create a shared conversation snapshot
    public async Task<string> ShareConversationAsync(Guid conversationId, ShareConversationRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/sharedconversations/{conversationId}", request);
        response.EnsureSuccessStatusCode();

        var sharedConversationId = await response.Content.ReadAsStringAsync();
        return sharedConversationId;
    }

    // ✅ Get public shared messages (read-only)
    public async Task<IList<SharedMessageDto>> GetPublicSharedConversationAsync(Guid shareId)
    {
        var response = await _httpClient.GetAsync($"/api/sharedconversations/public/{shareId}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<SharedMessageDto>>();
        return result ?? [];
    }

    // ✅ Get list of shared conversations by current user
    public async Task<IList<SharedConversationListItem>> GetMySharedConversationsAsync()
    {
        var response = await _httpClient.GetAsync("/api/sharedconversations/mine");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<SharedConversationListItem>>();
        return result ?? [];
    }

    // ✅ Unshare a conversation (soft delete)
    public async Task<bool> UnshareConversationAsync(Guid shareId)
    {
        var response = await _httpClient.DeleteAsync($"/api/sharedconversations/unshare/{shareId}");
        return response.IsSuccessStatusCode;
    }

    // ✅ Hard delete a shared conversation
    public async Task<bool> DeleteSharedConversationAsync(Guid shareId)
    {
        var response = await _httpClient.DeleteAsync($"/api/sharedconversations/{shareId}");
        return response.IsSuccessStatusCode;
    }

    // ✅ Update note on a shared conversation
    public async Task<bool> UpdateSharedConversationNoteAsync(Guid shareId, string? newNote)
    {
        var payload = new { Note = newNote };
        var response = await _httpClient.PutAsJsonAsync($"/api/sharedconversations/{shareId}", payload);
        return response.IsSuccessStatusCode;
    }
}
