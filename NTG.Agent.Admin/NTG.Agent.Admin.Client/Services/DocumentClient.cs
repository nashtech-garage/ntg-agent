using Microsoft.AspNetCore.Components.Forms;
using NTG.Agent.Shared.Dtos.Documents;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class DocumentClient(HttpClient httpClient)
{
    public async Task<IList<DocumentListItem>> GetDocumentsByAgentIdAsync(Guid agentId)
    {
        var response = await httpClient.GetAsync($"api/documents/{agentId}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<DocumentListItem>>();
        return result ?? [];
    }

    public async Task UploadDocumentsAsync(Guid agentId, IList<IBrowserFile> files)
    {
        long maxFileSize = 50 * 1024L * 1024L; // 50 MB
        using var content = new MultipartFormDataContent();
        foreach (var file in files)
        {
            if (file.Size > 0)
            {
                var fileContent = new StreamContent(file.OpenReadStream(maxFileSize));
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                content.Add(fileContent, "files", file.Name);
            }
        }
        var response = await httpClient.PostAsync($"api/documents/upload/{agentId}", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteDocumentByIdAsync(Guid agentId, Guid documentId)
    {
        var response = await httpClient.DeleteAsync($"api/documents/{documentId}/{agentId}");
        response.EnsureSuccessStatusCode();
    }
    public async Task<string> ImportWebPageAsync(Guid agentId, string url)
    {
        var request = new { Url = url };
        var response = await httpClient.PostAsJsonAsync($"api/documents/import-webpage/{agentId}", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return result;
    }
}
