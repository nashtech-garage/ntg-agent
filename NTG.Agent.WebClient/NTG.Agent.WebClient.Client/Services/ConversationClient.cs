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
}
