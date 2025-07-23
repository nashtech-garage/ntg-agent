using NTG.Agent.Shared.Dtos.Documents;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class DocumentClient(HttpClient httpClient)
{
    public async Task<IList<DocumentListItem>> GetDocumentsByAgentIdAsync(Guid agentId)
    {
        var response = await httpClient.GetAsync($"api/documents/{agentId}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<DocumentListItem>>();
        return result ?? [];
    }
}
