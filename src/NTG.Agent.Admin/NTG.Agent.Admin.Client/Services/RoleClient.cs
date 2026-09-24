using NTG.Agent.Common.Dtos.Tags;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class RoleClient(HttpClient httpClient)
{
    public async Task<List<RoleDto>> GetAllAsync()
    {
        var res = await httpClient.GetAsync("api/agentadmin/roles");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<RoleDto>>() ?? [];
    }
}
