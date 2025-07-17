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

    public async Task<List<ConversationSummary>> GetConversations()
    {
        var response = await httpClient.GetAsync("/api/conversations");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<List<ConversationSummary>>();
        return result ?? new List<ConversationSummary>();
    }

    public async Task<ConversationDetails?> GetConversationDetails(Guid id)
    {
        var response = await httpClient.GetAsync($"/api/conversations/{id}");
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<ConversationDetails>();
        }
        return null;
    }
}
