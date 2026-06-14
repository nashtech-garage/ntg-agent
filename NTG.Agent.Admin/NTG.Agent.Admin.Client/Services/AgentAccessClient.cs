using NTG.Agent.Common.Dtos.Agents;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class AgentAccessClient(HttpClient httpClient)
{
    public async Task<List<AgentRoleGrantDto>> ListAsync(Guid agentId)
    {
        var res = await httpClient.GetAsync($"api/agentadmin/{agentId}/access");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<AgentRoleGrantDto>>() ?? [];
    }

    public async Task<AgentRoleGrantDto> GrantAsync(Guid agentId, Guid roleId)
    {
        var res = await httpClient.PostAsJsonAsync(
            $"api/agentadmin/{agentId}/access",
            new GrantAgentAccessRequest(roleId));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AgentRoleGrantDto>())!;
    }

    public async Task RevokeAsync(Guid agentId, Guid roleId)
    {
        var res = await httpClient.DeleteAsync($"api/agentadmin/{agentId}/access/{roleId}");
        res.EnsureSuccessStatusCode();
    }
}
