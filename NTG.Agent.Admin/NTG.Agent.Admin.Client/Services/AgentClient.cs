using NTG.Agent.Common.Dtos.Agents;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class AgentClient(HttpClient httpClient)
{
    public async Task<IList<AgentListItem>> GetListAsync(AgentKind? agentKind = null)
    {
        string url = "api/agentadmin";
        if (agentKind.HasValue)
        {
            url += $"?agentKind={agentKind.Value}";
        }

        var response = await httpClient.GetAsync(url);
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

    public async Task<IList<ProviderDto>> GetProvidersAsync()
    {
        var response = await httpClient.GetAsync("api/agentadmin/providers");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<ProviderDto>>();
        return result ?? [];
    }

    public async Task<ProviderDto?> GetProviderAsync(Guid id)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/providers/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProviderDto>();
    }

    public async Task<Guid> CreateProviderAsync(ProviderDto provider)
    {
        var response = await httpClient.PostAsJsonAsync("api/agentadmin/providers", provider);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    public async Task UpdateProviderAsync(ProviderDto provider)
    {
        var response = await httpClient.PutAsJsonAsync($"api/agentadmin/providers/{provider.Id}", provider);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteProviderAsync(Guid id)
    {
        var response = await httpClient.DeleteAsync($"api/agentadmin/providers/{id}");
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(error);
        }
    }

    public async Task<TestConnectionResult> TestProviderConnectionAsync(Guid id)
    {
        var response = await httpClient.PostAsync($"api/agentadmin/providers/{id}/test", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TestConnectionResult>();
        return result ?? new TestConnectionResult { Success = false, ErrorMessage = "Empty response" };
    }

    public async Task<IList<ModelItem>> GetProviderModelsAsync(Guid id)
    {
        var response = await httpClient.GetAsync($"api/agentadmin/providers/{id}/models");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<ModelItem>>();
        return result ?? [];
    }
}