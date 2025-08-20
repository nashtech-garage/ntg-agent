using NTG.Agent.Shared.Dtos.Tags;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class TagClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    // Get all tags with optional search query
    public async Task<List<TagDto>> GetTagsAsync(string? searchQuery = null, CancellationToken cancellationToken = default)
    {
        var url = "api/tags";
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            url += $"?q={Uri.EscapeDataString(searchQuery)}";
        }

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("User is not authenticated");
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException("User does not have Admin role");
            }
            
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<List<TagDto>>(cancellationToken) ?? new List<TagDto>();
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Failed to fetch tags: {ex.Message}", ex);
        }
    }

    // Get tag by ID
    public async Task<TagDto?> GetTagByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/tags/{id}", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<TagDto>(cancellationToken);
        }
        return null;
    }

    // Create a new tag
    public async Task<TagDto> CreateTagAsync(TagCreateDto createDto, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/tags", createDto, cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"BadRequest: {errorContent}", null, response.StatusCode);
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Conflict: {errorContent}", null, response.StatusCode);
            }
            
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<TagDto>(cancellationToken) 
                ?? throw new InvalidOperationException("Failed to create tag");
        }
        catch (HttpRequestException ex) when (ex.Data.Contains("StatusCode"))
        {
            throw; // Re-throw HTTP exceptions with status codes
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Failed to create tag: {ex.Message}", ex);
        }
    }

    // Update a tag
    public async Task UpdateTagAsync(Guid id, TagUpdateDto updateDto, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"api/tags/{id}", updateDto, cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"BadRequest: {errorContent}", null, response.StatusCode);
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Conflict: {errorContent}", null, response.StatusCode);
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new HttpRequestException("NotFound: Tag not found", null, response.StatusCode);
            }
            
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex) when (ex.Data.Contains("StatusCode"))
        {
            throw; // Re-throw HTTP exceptions with status codes
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Failed to update tag: {ex.Message}", ex);
        }
    }

    // Delete a tag
    public async Task DeleteTagAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/tags/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // Get roles for a tag
    public async Task<List<TagRoleDto>> GetRolesForTagAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/tags/{tagId}/roles", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<List<TagRoleDto>>(cancellationToken) ?? new List<TagRoleDto>();
    }

    // Attach role to tag
    public async Task<TagRoleDto> AttachRoleToTagAsync(Guid tagId, TagRoleAttachDto attachDto, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/tags/{tagId}/roles", attachDto, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<TagRoleDto>(cancellationToken) 
            ?? throw new InvalidOperationException("Failed to attach role to tag");
    }

    // Detach role from tag
    public async Task DetachRoleFromTagAsync(Guid tagId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/tags/{tagId}/roles/{roleId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // Get available roles
    public async Task<List<RoleDto>> GetAvailableRolesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/tags/available-roles", cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("User is not authenticated");
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException("User does not have Admin role");
            }
            
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<List<RoleDto>>(cancellationToken) ?? new List<RoleDto>();
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Failed to fetch available roles: {ex.Message}", ex);
        }
    }
}
