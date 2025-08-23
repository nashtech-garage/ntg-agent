using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Knowledge;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.ServiceDefaults.Logging;
using NTG.Agent.ServiceDefaults.Logging.Metrics;
using NTG.Agent.Shared.Dtos.Documents;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class DocumentsController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IApplicationLogger<DocumentsController> _logger;
    private readonly IMetricsCollector _metrics;

    public DocumentsController(AgentDbContext agentDbContext, IKnowledgeService knowledgeService, IApplicationLogger<DocumentsController> logger, IMetricsCollector metrics)
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }
    /// <summary>
    /// Retrieves a list of documents associated with a specific agent and optionally filtered by a folder.
    /// </summary>
    /// <remarks>This method requires the caller to be authorized. The returned documents are represented as 
    /// <c>DocumentListItem</c> objects, which include basic metadata such as the document ID, name,  creation date, and
    /// last updated date.</remarks>
    /// <param name="agentId">The unique identifier of the agent whose documents are being retrieved.</param>
    /// <param name="folderId">The unique identifier of the folder to filter the documents by. If <see langword="null"/>,  documents not
    /// associated with any folder or associated with the root folder will be retrieved.</param>
    /// <returns>An <see cref="IActionResult"/> containing a list of documents. The list includes documents  associated with the
    /// specified folder or, if <paramref name="folderId"/> is <see langword="null"/>,  documents in the root folder or
    /// without a folder.</returns>
    [HttpGet("{agentId}")]
    [Authorize]
    public async Task<IActionResult> GetDocumentsByAgentId(Guid agentId, Guid? folderId)
    {
        using var scope = _logger.BeginScope("GetDocuments", new { AgentId = agentId });
        using var timer = _metrics.StartTimer("documents.get", ("agent_id", agentId.ToString()));

        var isRootfolder = await _agentDbContext.Folders
            .Where(f => f.Id == folderId && f.AgentId == agentId && f.ParentId == null)
            .FirstOrDefaultAsync();
        if (isRootfolder is not null)
        {
            // If the folder is the root folder, we return all documents that are either in the root folder or not associated with any folder.
            var defaultDocuments = await _agentDbContext.Documents
                .Include(x => x.DocumentTags)
                .ThenInclude(dt => dt.Tag)
                .Where(x => x.AgentId == agentId && (x.FolderId == folderId || x.FolderId == null))
                .Select(x => new DocumentListItem(
                    x.Id,
                    x.Name,
                    x.CreatedAt,
                    x.UpdatedAt,
                    x.DocumentTags.Select(dt => dt.Tag.Name).ToList()))
                .ToListAsync();
            return Ok(defaultDocuments);
        }
        var documents = await _agentDbContext.Documents
        .Include(x => x.DocumentTags)
        .ThenInclude(dt => dt.Tag)
        .Where(x => x.AgentId == agentId && x.FolderId == folderId)
        .Select(x => new DocumentListItem(
            x.Id,
            x.Name,
            x.CreatedAt,
            x.UpdatedAt,
            x.DocumentTags.Select(dt => dt.Tag.Name).ToList()))
        .ToListAsync();

        _logger.LogBusinessEvent("DocumentsRetrieved", new { AgentId = agentId, DocumentCount = documents.Count });
        _metrics.RecordBusinessMetric("DocumentsRetrieved", new { AgentId = agentId, documents.Count });
        return Ok(documents);
    }
    /// <summary>
    /// Uploads one or more documents for a specified agent and optionally associates them with a folder.
    /// </summary>
    /// <remarks>This method requires the user to be authenticated and authorized. If the user is not
    /// authenticated, an <see cref="UnauthorizedAccessException"/> is thrown. Each uploaded file is processed and
    /// stored as a document associated with the specified agent. The documents are saved in the database, and metadata
    /// such as the file name, creation time, and user information is recorded.</remarks>
    /// <param name="agentId">The unique identifier of the agent to associate the uploaded documents with.</param>
    /// <param name="files">A collection of files to be uploaded. Each file must have a non-zero length.</param>
    /// <param name="folderId">An optional unique identifier of the folder to associate the uploaded documents with. If not provided, the
    /// documents will not be associated with any folder.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation. Returns: <list type="bullet">
    /// <item><description><see cref="BadRequestObjectResult"/> if no files are provided or the files collection is
    /// empty.</description></item> <item><description><see cref="OkObjectResult"/> with a success message if the files
    /// are uploaded successfully.</description></item> </list></returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [HttpPost("upload/{agentId}")]
    [Authorize]
    public async Task<IActionResult> UploadDocuments(Guid agentId, [FromForm] IFormFileCollection files, [FromQuery] Guid? folderId, [FromQuery] List<string> tags)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest("No files uploaded.");
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        var documents = new List<Document>();
        var documentTags = new List<DocumentTag>();
        foreach (var file in files)
        {
            if (file.Length > 0)
            {
                var knowledgeDocId = await _knowledgeService.ImportDocumentAsync(file.OpenReadStream(), file.FileName, agentId, tags);
                var document = new Document
                {
                    Id = Guid.NewGuid(),
                    Name = file.FileName,
                    AgentId = agentId,
                    KnowledgeDocId = knowledgeDocId,
                    FolderId = folderId,
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Type = DocumentType.File
                };
                documents.Add(document);
                foreach (var tag in tags)
                {
                    var documentTag = new DocumentTag
                    {
                        DocumentId = document.Id,
                        TagId = new Guid(tag)
                    };
                    documentTags.Add(documentTag);
                }
            }
        }

        if (documents.Any())
        {
            _agentDbContext.Documents.AddRange(documents);
            _agentDbContext.DocumentTags.AddRange(documentTags);
            await _agentDbContext.SaveChangesAsync();
        }

        return Ok(new { message = "Files uploaded successfully." });
    }
    /// <summary>
    /// Deletes a document with the specified identifier.
    /// </summary>
    /// <remarks>This method requires the user to be authenticated and authorized. If the document is
    /// associated with a knowledge base,  it will also remove the document from the knowledge base before deleting it
    /// from the database.</remarks>
    /// <param name="id">The unique identifier of the document to delete.</param>
    /// <param name="agentId">The unique identifier of the agent associated with the document.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation: <list type="bullet">
    /// <item><description><see cref="UnauthorizedResult"/> if the user is not authenticated.</description></item>
    /// <item><description><see cref="NotFoundResult"/> if the document with the specified <paramref name="id"/> does
    /// not exist.</description></item> <item><description><see cref="NoContentResult"/> if the document is successfully
    /// deleted.</description></item> </list></returns>
    [HttpDelete("{id}/{agentId}")]
    [Authorize]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid agentId)
    {
        if (User.GetUserId() == null)
        {
            return Unauthorized();
        }

        var document = await _agentDbContext.Documents.FindAsync(id);

        if (document == null)
        {
            return NotFound();
        }

        if (document.KnowledgeDocId != null)
        {
            await _knowledgeService.RemoveDocumentAsync(document.KnowledgeDocId, agentId);
        }

        _agentDbContext.Documents.Remove(document);
        await _agentDbContext.SaveChangesAsync();

        return NoContent();
    }
    /// <summary>
    /// Imports a webpage into the system and associates it with the specified agent.
    /// </summary>
    /// <remarks>This method requires the user to be authenticated. The URL provided in the request must not
    /// be null, empty,  or consist only of whitespace. If the import is successful, the webpage is stored as a document
    /// in the database  and associated with the specified agent and folder (if provided).</remarks>
    /// <param name="agentId">The unique identifier of the agent to associate the imported webpage with.</param>
    /// <param name="request">The request containing the URL of the webpage to import and optional folder information.</param>
    /// <returns>An <see cref="IActionResult"/> containing the unique identifier of the imported document if successful,  or an
    /// error response if the operation fails.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [HttpPost("import-webpage/{agentId}")]
    [Authorize]
    public async Task<IActionResult> ImportWebPage(Guid agentId, [FromBody] ImportWebPageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest("URL is required.");
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        try
        {
            var documentId = await _knowledgeService.ImportWebPageAsync(request.Url, agentId, request.Tags);

            var document = new Document
            {
                Id = Guid.NewGuid(),
                Name = request.Url,
                AgentId = agentId,
                KnowledgeDocId = documentId,
                FolderId = request.FolderId,
                Url = request.Url,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Type = DocumentType.WebPage
            };

            var documentTags = new List<DocumentTag>();
            foreach (var tag in request.Tags)
            {
                var documentTag = new DocumentTag
                {
                    DocumentId = document.Id,
                    TagId = new Guid(tag)
                };
                documentTags.Add(documentTag);
            }

            _agentDbContext.Documents.Add(document);
            _agentDbContext.DocumentTags.AddRange(documentTags);
            await _agentDbContext.SaveChangesAsync();

            return Ok(documentId);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to import webpage: {ex.Message}");
        }
    }
    /// <summary>
    /// Uploads text content as a document for a specified agent and optionally associates it with a folder.
    /// </summary>
    /// <remarks>This method requires the user to be authenticated and authorized. The text content is processed and
    /// stored as a document associated with the specified agent. The document is saved in the database, and metadata
    /// such as the title, creation time, and user information is recorded.</remarks>
    /// <param name="agentId">The unique identifier of the agent to associate the text content with.</param>
    /// <param name="request">The request containing the title and content of the text to upload, along with optional folder and tag information.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation. Returns: <list type="bullet">
    /// <item><description><see cref="BadRequestObjectResult"/> if the content is null or empty.</description></item>
    /// <item><description><see cref="OkObjectResult"/> with the document ID if the text is uploaded successfully.</description></item> 
    /// </list></returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [HttpPost("upload-text/{agentId}")]
    [Authorize]
    public async Task<IActionResult> UploadTextContent(Guid agentId, [FromBody] UploadTextContentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Content is required.");
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        try
        {
            var title = string.IsNullOrWhiteSpace(request.Title) ? "Text Content" : request.Title;
            var knowledgeDocId = await _knowledgeService.ImportTextContentAsync(request.Content, title, agentId, request.Tags);

            var document = new Document
            {
                Id = Guid.NewGuid(),
                Name = title,
                AgentId = agentId,
                KnowledgeDocId = knowledgeDocId,
                FolderId = request.FolderId,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Type = DocumentType.Text
            };

            var documentTags = new List<DocumentTag>();
            foreach (var tag in request.Tags)
            {
                var documentTag = new DocumentTag
                {
                    DocumentId = document.Id,
                    TagId = new Guid(tag)
                };
                documentTags.Add(documentTag);
            }

            _agentDbContext.Documents.Add(document);
            _agentDbContext.DocumentTags.AddRange(documentTags);
            await _agentDbContext.SaveChangesAsync();

            return Ok(document.Id.ToString());
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to upload text content: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads a document by its unique identifier and associated agent.
    /// </summary>
    /// <remarks>This method requires the user to be authenticated and authorized. The method supports downloading
    /// different types of documents: <list type="bullet">
    /// <item><description><strong>File documents:</strong> Returns the original file content with appropriate MIME type.</description></item>
    /// <item><description><strong>Text documents:</strong> Returns the text content as a plain text file with .txt extension.</description></item>
    /// <item><description><strong>WebPage documents:</strong> Fetches and returns the content from the stored URL with appropriate content type and file extension.</description></item>
    /// </list>
    /// The response includes proper content-type headers and sanitized filenames for safe downloading.</remarks>
    /// <param name="id">The unique identifier of the document to download.</param>
    /// <param name="agentId">The unique identifier of the agent associated with the document.</param>
    /// <returns>An <see cref="IActionResult"/> containing the document content as a file download. Returns:
    /// <list type="bullet">
    /// <item><description><see cref="NotFoundResult"/> if the document with the specified <paramref name="id"/> and <paramref name="agentId"/> does not exist.</description></item>
    /// <item><description><see cref="NotFoundResult"/> if the document content cannot be retrieved from the knowledge service.</description></item>
    /// <item><description><see cref="FileResult"/> with the document content, appropriate MIME type, and filename if successful.</description></item>
    /// <item><description><see cref="StatusCodeResult"/> with status 500 if an error occurs while accessing the file content or downloading from a URL.</description></item>
    /// </list></returns>
    [HttpGet("download/{agentId}/{id}")]
    [Authorize]
    public async Task<IActionResult> GetDocumentById(Guid id, Guid agentId, CancellationToken ct)
    {
        var document = await _agentDbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.AgentId == agentId, ct);

        if (document is null) return NotFound();

        return document.Type switch
        {
            DocumentType.File or DocumentType.Text => await HandleKnowledgeFileDownloadAsync(document, agentId, ct),
            DocumentType.WebPage => await HandleWebPageDownloadAsync(document, ct),
            _ => NotFound("Unsupported document type.")
        };
    }

    private async Task<IActionResult> HandleKnowledgeFileDownloadAsync(Document document, Guid agentId, CancellationToken ct)
    {
        if (document.KnowledgeDocId is null) return NotFound("No knowledge document id.");

        var isText = document.Type == DocumentType.Text;
        var baseName = SanitizeFileName(document.Name);
        var fileName = isText ? $"{baseName}.txt" : baseName;

        var contentType = isText
            ? "text/plain"
            : GetContentType(fileName);

        var content = await _knowledgeService.ExportDocumentAsync(
            document.KnowledgeDocId, fileName, agentId);

        if (content is null) return NotFound();

        var stream = await content.GetStreamAsync();
        return File(stream, contentType, fileName);
    }

    private async Task<IActionResult> HandleWebPageDownloadAsync(Document document, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(document.Url))
            return NotFound("Webpage URL not found.");

        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri))
            return BadRequest("Invalid webpage URL.");

        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(document.Url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStreamAsync();

            // Prefer server-provided content-type, with fallback to URL/extension
            var headerType = response.Content.Headers.ContentType?.MediaType;
            var inferredType = headerType ?? GetContentTypeFromUrlPath(uri.AbsolutePath);

            // File extension from content-type or URL
            var extension = GetFileExtensionFromContentType(inferredType, uri.ToString());
            var fileName = $"{SanitizeFileName(document.Name)}{extension}";
            var stream = await response.Content.ReadAsStreamAsync(ct);
            return File(stream, inferredType, fileName);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to download content from URL: {ex.Message}");
        }
    }

    private static readonly FileExtensionContentTypeProvider _mimeProvider = new();

    private static string GetContentType(string fileName)
    {
        if (_mimeProvider.TryGetContentType(fileName, out var contentType))
            return contentType;

        return GetContentTypeFromExtension(Path.GetExtension(fileName));
    }

    private static string GetContentTypeFromUrlPath(string urlPath)
    {
        var fileName = Path.GetFileName(urlPath);
        if (!string.IsNullOrEmpty(fileName) && _mimeProvider.TryGetContentType(fileName, out var contentType))
            return contentType;

        return "application/octet-stream";
    }

    private static string GetContentTypeFromExtension(string extension)
        => (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".htm" or ".html" => "text/html",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "download";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());

        sanitized = sanitized
            .Replace("://", "_")
            .Replace("/", "_")
            .Replace("?", "_")
            .Replace("&", "_")
            .Replace("=", "_")
            .Replace("#", "_");

        if (sanitized.Length > 120) sanitized = sanitized[..120];
        return string.IsNullOrWhiteSpace(sanitized) ? "download" : sanitized;
    }

    private static string GetFileExtensionFromContentType(string contentType, string url)
    {
        try
        {
            var urlExt = Path.GetExtension(new Uri(url).AbsolutePath);
            if (!string.IsNullOrEmpty(urlExt)) return urlExt;
        }
        catch { /* ignore */ }

        return (contentType ?? string.Empty).ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/vnd.ms-powerpoint" => ".ppt",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            "application/json" => ".json",
            "application/xml" or "text/xml" => ".xml",
            "text/html" => ".html",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "application/zip" => ".zip",
            _ => ".html"
        };
    }
}

public record ImportWebPageRequest(string Url, Guid? FolderId, List<string> Tags);
public record UploadTextContentRequest(string Title, string Content, Guid? FolderId, List<string> Tags);
