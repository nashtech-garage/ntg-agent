using NTG.Agent.WebClient.Client.ViewModels;
using System.Net.Http.Json;

namespace NTG.Agent.WebClient.Client.Services;

public class ChatClient(HttpClient httpClient)
{
    private const string REQUEST_URI = "/api/agents/chat";

    public async Task<IAsyncEnumerable<PromptResponse>> InvokeStreamAsync(PromptRequest content)
    {
        var response = await httpClient.PostAsJsonAsync<PromptRequest>(REQUEST_URI, content);
        response.EnsureSuccessStatusCode();

        var result = response.Content.ReadFromJsonAsAsyncEnumerable<PromptResponse>();

        return result!;
    }
}
