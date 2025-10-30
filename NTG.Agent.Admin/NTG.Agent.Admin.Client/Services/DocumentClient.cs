using Microsoft.AspNetCore.Components.Forms;
using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.Common.Dtos.Services;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class DocumentClient(HttpClient httpClient)
{
    public async Task<IList<DocumentListItem>> GetDocumentsByAgentIdAsync(Guid agentId, Guid? folderId)
    {
        var response = await httpClient.GetAsync($"api/documents/{agentId}?folderId={folderId}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<DocumentListItem>>();
        return result ?? [];
    }

    public async Task UploadDocumentsAsync(Guid agentId, IList<IBrowserFile> files, Guid? folderId, List<string> tags)
    {
        long maxFileSize = 50 * 1024L * 1024L; // 50 MB
        using var content = new MultipartFormDataContent();
        foreach (var file in files)
        {
            if (file.Size > 0)
            {
                var fileContent = new StreamContent(file.OpenReadStream(maxFileSize));
                
                // Get content type from file or fallback to detection by extension
                var contentType = !string.IsNullOrEmpty(file.ContentType) 
                    ? file.ContentType 
                    : FileTypeService.GetContentType(file.Name);
                    
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                content.Add(fileContent, "files", file.Name);
            }
        }
        
        var queryParams = new List<string>();
        if (folderId.HasValue)
            queryParams.Add($"folderId={folderId}");
        
        if (tags != null && tags.Any())
        {
            foreach (var tag in tags)
            {
                queryParams.Add($"tags={Uri.EscapeDataString(tag)}");
            }
        }
        
        var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
        var response = await httpClient.PostAsync($"api/documents/upload/{agentId}{queryString}", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteDocumentByIdAsync(Guid agentId, Guid documentId)
    {
        var response = await httpClient.DeleteAsync($"api/documents/{documentId}/{agentId}");
        response.EnsureSuccessStatusCode();
    }
    public async Task<string> ImportWebPageAsync(Guid agentId, string url, Guid? folderId, List<string> tags)
    {
        var request = new { Url = url , FolderId = folderId, Tags = tags};
        var response = await httpClient.PostAsJsonAsync($"api/documents/import-webpage/{agentId}", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return result;
    }

    public async Task<string> UploadTextContentAsync(Guid agentId, string title, string content, Guid? folderId, List<string> tags)
    {
        var request = new { Title = title, Content = content, FolderId = folderId, Tags = tags };
        var response = await httpClient.PostAsJsonAsync($"api/documents/upload-text/{agentId}", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        return result;
    }

    public async Task<(Stream Content, string FileName, string ContentType)> DownloadDocumentAsync(Guid agentId, Guid documentId)
    {
        var response = await httpClient.GetAsync($"api/documents/download/{agentId}/{documentId}");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync();
        
        // Extract filename from Content-Disposition header if available
        var fileName = "document";
        if (response.Content.Headers.ContentDisposition?.FileName != null)
        {
            fileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
        }
        
        // Get content type from response headers
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        
        return (content, fileName, contentType);
    }

    public async Task<(string Content, string ContentType)> ViewDocumentAsync(Guid agentId, Guid documentId)
    {
        var maxPreviewFileSizeBytes = 30 * 1024 * 1024; // 30 MB limit for preview
        var response = await httpClient.GetAsync($"api/documents/download/{agentId}/{documentId}");
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        // Check content length to avoid loading very large files
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxPreviewFileSizeBytes)
        {
            throw new InvalidOperationException($"Document is too large to preview (> {maxPreviewFileSizeBytes}MB)");
        }

        // Only read as string for text-based content types
        if (FileTypeService.IsTextBasedContentType(contentType))
        {
            var content = await response.Content.ReadAsStringAsync();
            return (content, contentType);
        }
        else
        {
            throw new InvalidOperationException($"Content type '{contentType}' is not suitable for text preview");
        }
    }
}
