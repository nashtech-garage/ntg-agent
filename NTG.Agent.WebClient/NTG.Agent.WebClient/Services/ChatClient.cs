using NTG.Agent.WebClient.ViewModels;

namespace NTG.Agent.WebClient.Services;

public class ChatClient(HttpClient httpClient)
{
    private const string REQUEST_URI = "api/agents/chat";

    public async Task<IAsyncEnumerable<PromptResponse>> InvokeStreamAsync(PromptRequest content)              
    {
        var response = await httpClient.PostAsJsonAsync<PromptRequest>(REQUEST_URI, content);
        response.EnsureSuccessStatusCode();

        var result = response.Content.ReadFromJsonAsAsyncEnumerable<PromptResponse>();

        return result!;
    }
}
