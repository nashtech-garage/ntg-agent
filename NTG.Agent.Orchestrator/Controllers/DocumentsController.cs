using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Shared.Dtos.Documents;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Knowledge;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class DocumentsController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    private readonly IKnowledgeService _knowledgeService;

    public DocumentsController(AgentDbContext agentDbContext, IKnowledgeService knowledgeService)
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
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
        var isRootfolder = await _agentDbContext.Folders
            .Where(f => f.Id == folderId && f.AgentId == agentId && f.ParentId == null)
            .FirstOrDefaultAsync();
        if (isRootfolder is not null)
        {
            // If the folder is the root folder, we return all documents that are either in the root folder or not associated with any folder.
            var defaultDocuments = await _agentDbContext.Documents
                .Where(x => x.AgentId == agentId && (x.FolderId == folderId || x.FolderId == null))
                .Select(x => new DocumentListItem(x.Id, x.Name, x.CreatedAt, x.UpdatedAt))
                .ToListAsync();
            return Ok(defaultDocuments);
        }
        var documents = await _agentDbContext.Documents
        .Where(x => x.AgentId == agentId && x.FolderId == folderId)
        .Select(x => new DocumentListItem(x.Id, x.Name, x.CreatedAt, x.UpdatedAt))
        .ToListAsync();
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
    public async Task<IActionResult> UploadDocuments(Guid agentId, [FromForm] IFormFileCollection files, [FromQuery] Guid? folderId)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest("No files uploaded.");
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        var documents = new List<Document>();

        foreach (var file in files)
        {
            if (file.Length > 0)
            {
                var knowledgeDocId = await _knowledgeService.ImportDocumentAsync(file.OpenReadStream(), file.FileName, agentId);
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
            }
        }

        if (documents.Any())
        {
            _agentDbContext.Documents.AddRange(documents);
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
            var documentId = await _knowledgeService.ImportWebPageAsync(request.Url, agentId);

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

            _agentDbContext.Documents.Add(document);
            await _agentDbContext.SaveChangesAsync();

            return Ok(documentId);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to import webpage: {ex.Message}");
        }
    }
}

public record ImportWebPageRequest(string Url, Guid? FolderId);
