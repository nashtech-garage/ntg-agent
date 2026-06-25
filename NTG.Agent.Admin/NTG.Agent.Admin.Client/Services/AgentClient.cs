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

    // Agent-as-a-tool management

    /// <summary>
    /// Gets all published agents that can be linked as tools to the given parent agent.
    /// </summary>
    public async Task<IList<LinkableAgentDto>> GetLinkableAgentsAsync(Guid agentId)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/{agentId}/linkable-agents");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<LinkableAgentDto>>();
        return result ?? [];
    }

    /// <summary>
    /// Links a child agent as a tool on the parent agent.
    /// </summary>
    public async Task<AgentToolDto> AddAgentToolAsync(Guid agentId, AddAgentToolRequest request)
    {
        var response = await httpClient.PostAsJsonAsync($"api/agentadmin/{agentId}/agent-tools", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AgentToolDto>();
        return result ?? throw new InvalidOperationException("Failed to deserialize agent tool response.");
    }

    /// <summary>
    /// Removes an agent-type tool link from the parent agent.
    /// </summary>
    public async Task RemoveAgentToolAsync(Guid agentId, Guid toolId)
    {
        var response = await httpClient.DeleteAsync($"api/agentadmin/{agentId}/agent-tools/{toolId}");
        response.EnsureSuccessStatusCode();
    }
}
