using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Conversations;
using System.Net;
using System.Net.Http.Json;

namespace NTG.Agent.WebClient.Client.Services;

public class ConversationClient(HttpClient httpClient)
{
    public async Task<ConversationCreated> Create(string currentSessionId)
    {
            string url = string.IsNullOrWhiteSpace(currentSessionId)
            ? "/api/conversations"
            : $"/api/conversations?currentSessionId={currentSessionId}";

            var response = await httpClient.PostAsync(url, null);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ConversationCreated>();
            return result!;
    }


    public async Task<IList<ConversationListItem>> GetConversationsAsync()
    {
        var response = await httpClient.GetAsync("/api/conversations");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<ConversationListItem>>();
        return result ?? [];
    }

    public async Task<IList<ChatMessageListItem>> GetConversationMessagesAsync(Guid conversationId, string currentSessionId)
    {
        string url = string.IsNullOrWhiteSpace(currentSessionId)
            ? $"/api/conversations/{conversationId}/messages"
            : $"/api/conversations/{conversationId}/messages?currentSessionId={Uri.EscapeDataString(currentSessionId)}";

        var response = await httpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Access denied to this conversation.");
        }
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<ChatMessageListItem>>();
        return result ?? [];
    }



    public async Task<bool> DeleteConversationAsync(Guid conversationId)
    {
        var response = await httpClient.DeleteAsync($"/api/conversations/{conversationId}");
        response.EnsureSuccessStatusCode();
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateConversationAsync(Guid conversationId, string newName)
    {
        var response = await httpClient.PutAsync($"/api/conversations/{conversationId}/rename?newName={Uri.EscapeDataString(newName)}", null);
        response.EnsureSuccessStatusCode();
        return response.IsSuccessStatusCode;
    }

    public async Task<IList<ChatSearchResultItem>> SearchChatMessages(string keyword)
    {
        var response = await httpClient.GetAsync($"/api/conversations/search?keyword={Uri.EscapeDataString(keyword)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<ChatSearchResultItem>>();
        return result ?? [];
    }
}