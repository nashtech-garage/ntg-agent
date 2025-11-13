using NTG.Agent.Common.Dtos.Agents;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class AgentClient(HttpClient httpClient)
{
    public async Task<IList<AgentListItem>> GetListAsync()
    {
        var response = await httpClient.GetAsync("api/agentadmin");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<AgentListItem>>();
        return result ?? [];
    }

    public async Task<AgentDetail?> GetAgentDetails(Guid id)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/{id}");

        var result = await response.Content.ReadFromJsonAsync<AgentDetail>();
        return result;
    }

    public async Task UpdateAgent(AgentDetail agentDetail)
    {
        var response = await httpClient.PutAsJsonAsync($"api/agentadmin/{agentDetail.Id}", agentDetail);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateAgentToolsAsync(Guid agentId, IList<AgentToolDto> tools)
    {
        var response = await httpClient.PutAsJsonAsync($"api/agentadmin/{agentId}/tools", tools);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IList<AgentToolDto>> GetAgentToolsByAgentId(Guid id)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/{id}/tools");

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<AgentToolDto>>();
        return result ?? [];
    }

    public async Task<IList<AgentToolDto>> ConnectToMcpServerAsync(Guid id, string endpoint)
    {
        var response = await httpClient.PostAsJsonAsync($"api/agentadmin/{id}/connect",endpoint);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<AgentToolDto>>();
        return result ?? [];
    }

    public async Task<Guid> CreateAgent(AgentDetail agentDetail)
    {
        var response = await httpClient.PostAsJsonAsync($"api/agentadmin", agentDetail);
        response.EnsureSuccessStatusCode();
        
        var createdAgent = await response.Content.ReadFromJsonAsync<AgentDetail>();
        return createdAgent?.Id ?? Guid.Empty;
    }
}
