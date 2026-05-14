using Microsoft.AspNetCore.StaticFiles;
using NTG.Agent.Common.Dtos.Knowledge;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

public class LightRagFileStore
{
    private readonly string _basePath;
    private readonly ILogger<LightRagFileStore> _logger;
    private static readonly FileExtensionContentTypeProvider MimeProvider = new();

    public LightRagFileStore(string basePath, ILogger<LightRagFileStore> logger)
    {
        _basePath = basePath;
        _logger = logger;
    }

    // NOTE: Replace with Azure Blob Storage for production deployment.
    public async Task SaveAsync(Guid agentId, string documentId, string fileName, Stream content, CancellationToken ct = default)
    {
        var dir = Path.Combine(_basePath, agentId.ToString());
        Directory.CreateDirectory(dir);
        var filePath = BuildFilePath(agentId, documentId, fileName);
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await content.CopyToAsync(fs, ct);
        _logger.LogInformation("LightRagFileStore.SaveAsync: saved {FilePath}", filePath);
    }

    // NOTE: Replace with Azure Blob Storage for production deployment.
    // Caller is responsible for disposing the stream inside the returned KnowledgeFileContent.
    public KnowledgeFileContent? GetAsync(Guid agentId, string documentId, string fileName)
    {
        var filePath = BuildFilePath(agentId, documentId, fileName);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("LightRagFileStore.GetAsync: file not found {FilePath}", filePath);
            return null;
        }
        var contentType = MimeProvider.TryGetContentType(fileName, out var ct) ? ct : "application/octet-stream";
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return new KnowledgeFileContent(stream, contentType, fileName);
    }

    // NOTE: Replace with Azure Blob Storage for production deployment.
    // Globs {basePath}/{agentId}/{docId}_* to delete without needing the filename.
    public void FindAndDelete(Guid agentId, string documentId)
    {
        var dir = Path.Combine(_basePath, agentId.ToString());
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, $"{documentId}_*"))
        {
            File.Delete(file);
            _logger.LogInformation("LightRagFileStore.FindAndDelete: deleted {File}", file);
        }
    }

    private string BuildFilePath(Guid agentId, string documentId, string fileName)
        => Path.Combine(_basePath, agentId.ToString(), $"{documentId}_{fileName}");
}
