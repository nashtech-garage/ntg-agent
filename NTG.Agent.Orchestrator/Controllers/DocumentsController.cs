using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Orchestrator.Data;

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

}
