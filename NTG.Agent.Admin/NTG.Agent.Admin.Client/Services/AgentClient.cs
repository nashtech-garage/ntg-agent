using NTG.Agent.Shared.Dtos.Agents;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class AgentClient(HttpClient httpClient)
{
    public async Task<IList<AgentListItem>> GetListAsync()
    {
        var response = await httpClient.GetAsync("api/agents");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<AgentListItem>>();
        return result ?? [];
    }
}
