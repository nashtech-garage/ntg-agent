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
        
        var createdAgentId = await response.Content.ReadFromJsonAsync<Guid>();
        return createdAgentId;
    }

    public async Task DeleteAgent(Guid id)
    {
        var response = await httpClient.DeleteAsync($"api/agentadmin/{id}");
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Status: {(int)response.StatusCode}, Error: {errorContent}");
        }
    }

    public async Task UpdateAgentPublishStatus(Guid id, bool isPublished)
    {
        var response = await httpClient.PatchAsJsonAsync($"api/agentadmin/{id}/publish", isPublished);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IList<InnerAgentListItem>> GetInnerAgentsAsync()
    {
        var response = await httpClient.GetAsync("api/agentadmin/inner");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<InnerAgentListItem>>();
        return result ?? [];
    }

    public async Task<InnerAgentDetail?> GetInnerAgentAsync(Guid id)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/inner/{id}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<InnerAgentDetail>();
    }

    public async Task<Guid> CreateInnerAgentAsync(InnerAgentDetail agentDetail)
    {
        var response = await httpClient.PostAsJsonAsync("api/agentadmin/inner", agentDetail);
        response.EnsureSuccessStatusCode();

        var createdAgentId = await response.Content.ReadFromJsonAsync<Guid>();
        return createdAgentId;
    }

    public async Task UpdateInnerAgentAsync(InnerAgentDetail agentDetail)
    {
        var response = await httpClient.PutAsJsonAsync($"api/agentadmin/inner/{agentDetail.Id}", agentDetail);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteInnerAgentAsync(Guid id)
    {
        var response = await httpClient.DeleteAsync($"api/agentadmin/inner/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<IList<AgentToolDto>> GetInnerAgentToolsAsync(Guid id)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/inner/{id}/tools");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<AgentToolDto>>();
        return result ?? [];
    }

    public async Task UpdateInnerAgentToolsAsync(Guid agentId, IList<AgentToolDto> tools)
    {
        var response = await httpClient.PutAsJsonAsync($"api/agentadmin/inner/{agentId}/tools", tools);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IList<AgentToolDto>> ConnectInnerAgentToMcpServerAsync(Guid id, string endpoint)
    {
        var response = await httpClient.PostAsJsonAsync($"api/agentadmin/inner/{id}/connect", endpoint);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<AgentToolDto>>();
        return result ?? [];
    }

    public async Task<IList<InnerAgentBindingDto>> GetInnerAgentBindingsAsync(Guid agentId)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/{agentId}/inner-agents");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<InnerAgentBindingDto>>();
        return result ?? [];
    }

    public async Task UpdateInnerAgentBindingsAsync(Guid agentId, IList<InnerAgentBindingDto> bindings)
    {
        var response = await httpClient.PutAsJsonAsync($"api/agentadmin/{agentId}/inner-agents", bindings);
        response.EnsureSuccessStatusCode();
    }
}
