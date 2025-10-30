using NTG.Agent.Common.Dtos.SharedConversations;
using System.Net;
using System.Net.Http.Json;

public class SharedConversationClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    // ✅ Create a shared conversation snapshot
    public async Task<string> ShareConversationAsync(ShareConversationRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/sharedconversations", request);
        response.EnsureSuccessStatusCode();

        var sharedConversationId = await response.Content.ReadAsStringAsync();
        return sharedConversationId;
    }

    // ✅ Get public shared messages (read-only)
    public record SharedConversationResult(bool Success, IList<SharedMessageDto>? Messages, string? Reason);
    public async Task<SharedConversationResult> GetPublicSharedConversationAsync(Guid shareId)
    {
        var response = await _httpClient.GetAsync($"/api/sharedconversations/public/{shareId}?_ts={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var data = await response.Content.ReadFromJsonAsync<IList<SharedMessageDto>>();
            return new(true, data ?? [], null);
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => new(false, null, "NOT_FOUND"),
            HttpStatusCode.Forbidden => new(false, null, "INACTIVE"),
            HttpStatusCode.Gone => new(false, null, "EXPIRED"),
            _ => new(false, null, "ERROR")
        };
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
    public async Task<bool> UpdateSharedConversationAsync(Guid shareId, bool flag)
    {
        var response = await _httpClient.PutAsync($"/api/sharedconversations/update-share/{shareId}/{flag}", null);
        return response.IsSuccessStatusCode;
    }

    // ✅ Hard delete a shared conversation
    public async Task<bool> DeleteSharedConversationAsync(Guid shareId)
    {
        var response = await _httpClient.DeleteAsync($"/api/sharedconversations/{shareId}");
        return response.IsSuccessStatusCode;
    }

    // ✅ Update expiration date on a shared conversation
    public async Task<bool> UpdateSharedConversationExpirationAsync(Guid shareId, DateTime? expiresAt)
    {
        var payload = new UpdateExpirationRequest { ExpiresAt = expiresAt };
        var response = await _httpClient.PutAsJsonAsync($"/api/sharedconversations/{shareId}/expiration", payload);
        return response.IsSuccessStatusCode;
    }
}
