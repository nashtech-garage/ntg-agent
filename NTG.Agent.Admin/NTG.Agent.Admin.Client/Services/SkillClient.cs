using Microsoft.AspNetCore.Components.Forms;
using NTG.Agent.Common.Dtos.Skills;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

/// <summary>Thrown when POST api/skills/import returns 400 with a structured error list. Carries every
/// line-item validation message so the UI can render the full list instead of a generic failure.</summary>
public class SkillImportException : Exception
{
    public IList<string> Errors { get; }

    public SkillImportException(IList<string> errors)
        : base(errors.Count > 0 ? string.Join(" ", errors) : "The skill package was rejected.")
    {
        Errors = errors;
    }
}

public class SkillClient(HttpClient httpClient)
{
    // Mirrors SkillPackageImporter.MaxPackageBytes. Kept in step deliberately: a larger value here
    // uploads the whole file before the server rejects it, so the user waits to be told no.
    // This is a UX bound only — the server enforces the real one.
    private const long MaxPackageSizeBytes = 5 * 1024L * 1024L;

    public async Task<IList<SkillListItem>> GetListAsync()
    {
        var response = await httpClient.GetAsync("api/skills");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<SkillListItem>>();
        return result ?? [];
    }

    public async Task<SkillDetail?> GetDetailAsync(Guid id)
    {
        var response = await httpClient.GetAsync($"api/skills/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SkillDetail>();
    }

    /// <summary>Uploads a skill package. On a 400 response the API returns a list of line-item validation
    /// errors; those are surfaced via <see cref="SkillImportException"/> so the caller can render every
    /// message rather than a generic "import failed".</summary>
    public async Task<SkillDetail> ImportAsync(IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(file.OpenReadStream(MaxPackageSizeBytes));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", file.Name);

        var response = await httpClient.PostAsync("api/skills/import", content);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errorPayload = await response.Content.ReadFromJsonAsync<SkillImportErrorResponse>();
            var errors = errorPayload?.Errors is { Count: > 0 }
                ? errorPayload.Errors
                : ["The skill package was rejected."];
            throw new SkillImportException(errors);
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SkillDetail>()
            ?? throw new InvalidOperationException("Import succeeded but the server did not return the imported skill.");
    }

    public async Task DeleteAsync(Guid id)
    {
        var response = await httpClient.DeleteAsync($"api/skills/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<(Stream Content, string FileName, string ContentType)> ExportAsync(Guid id, string fallbackFileName)
    {
        var response = await httpClient.GetAsync($"api/skills/{id}/export");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync();

        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"{fallbackFileName}.zip";

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/zip";

        return (content, fileName, contentType);
    }

    public async Task<IList<AgentSkillDto>> GetAgentSkillsAsync(Guid agentId)
    {
        var response = await httpClient.GetAsync($"api/skills/agents/{agentId}/skills");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<AgentSkillDto>>();
        return result ?? [];
    }

    public async Task UpdateAgentSkillsAsync(Guid agentId, IList<AgentSkillDto> skills)
    {
        var response = await httpClient.PutAsJsonAsync($"api/skills/agents/{agentId}/skills", skills);
        response.EnsureSuccessStatusCode();
    }
}
