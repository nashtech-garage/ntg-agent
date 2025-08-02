using NTG.Agent.Shared.Dtos.Folders;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class FolderClient
{
    private readonly HttpClient _httpClient;

    public FolderClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IList<FolderItem>> GetFoldersByAgentIdAsync(Guid agentId)
    {
        var response = await _httpClient.GetAsync($"api/folders?agentId={agentId}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<FolderItem>>();
        return result ?? [];
    }

    public async Task<FolderItem?> GetFolderByIdAsync(Guid id)
    {
        var response = await _httpClient.GetAsync($"api/folders/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FolderItem>();
    }

    public async Task<FolderItem?> CreateFolderAsync(CreateFolderDto folder)
    {
        var response = await _httpClient.PostAsJsonAsync("api/folders", folder);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FolderItem>();
    }

    public async Task UpdateFolderAsync(Guid id, UpdateFolderDto folder)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/folders/{id}", folder);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteFolderAsync(Guid id)
    {
        var response = await _httpClient.DeleteAsync($"api/folders/{id}");
        response.EnsureSuccessStatusCode();
    }
}
