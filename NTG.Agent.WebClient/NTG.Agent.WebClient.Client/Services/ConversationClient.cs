using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Conversations;
using System.Net.Http.Json;

namespace NTG.Agent.WebClient.Client.Services;

public class ConversationClient(HttpClient httpClient)
{
    public async Task<ConversationCreated> Create()
    {
        var response = await httpClient.PostAsync("/api/conversations", null);
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

    public async Task<IList<ChatMessageListItem>> GetConversationMessagesAsync(Guid conversationId)
    {
        var response = await httpClient.GetAsync($"/api/conversations/{conversationId}/messages");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<ChatMessageListItem>>();
        return result ?? [];
    }
}
