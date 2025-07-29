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

    [HttpGet("{agentId}")]
    [Authorize]
    public async Task<IActionResult> GetDocumentsByAgentId(Guid agentId)
    {
        var documents = await _agentDbContext.Documents
            .Where(x => x.AgentId == agentId)
            .Select(x => new DocumentListItem(x.Id, x.Name, x.CreatedAt, x.UpdatedAt))
            .ToListAsync();
        return Ok(documents);
    }

    [HttpPost("upload/{agentId}")]
    [Authorize]
    public async Task<IActionResult> UploadDocuments(Guid agentId, [FromForm] IFormFileCollection files)
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
                await _knowledgeService.ImportDocument(file.OpenReadStream(), file.FileName, agentId);

                var document = new Document
                {
                    Id = Guid.NewGuid(),
                    Name = file.FileName,
                    AgentId = agentId,
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

public record ImportWebPageRequest(string Url);
