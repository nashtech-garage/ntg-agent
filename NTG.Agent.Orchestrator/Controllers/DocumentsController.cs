using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Shared.Dtos.Documents;

namespace NTG.Agent.Orchestrator.Controllers;
[Route("api/[controller]")]
[ApiController]
public class DocumentsController : ControllerBase
{
    private readonly AgentDbContext _agentDbContext;
    public DocumentsController(AgentDbContext agentDbContext)
    {
        _agentDbContext = agentDbContext ?? throw new ArgumentNullException(nameof(agentDbContext));
    }

    [HttpGet("{agentId}")]
    [Authorize]
    public async Task<IActionResult> GetDocumentsByAgentId(Guid agentId)
    {
        var documents = await _agentDbContext.Documents
            .Where(x => x.AgentId == agentId)
            .Select(x => new DocumentListItem (x.Id, x.Name, x.CreatedAt, x.UpdatedAt))
            .ToListAsync();
        return Ok(documents);
    }

}
