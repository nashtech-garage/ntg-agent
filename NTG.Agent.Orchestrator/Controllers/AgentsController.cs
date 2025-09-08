using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Shared.Dtos.Chats;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AgentsController : ControllerBase
{
    private readonly AgentService _agentService;
    private readonly AgentDbContext _agentDbContext;
    public AgentsController(AgentService agentService, AgentDbContext agentDbContext)
    {
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _agentDbContext = agentDbContext;
    }

    [HttpPost("chat")]
    public async IAsyncEnumerable<PromptResponse> ChatAsync([FromForm] PromptRequestForm promptRequest)
    {
        Guid? userId = User.GetUserId();
        await foreach (var response in _agentService.ChatStreamingAsync(userId, promptRequest))
        {
            yield return new PromptResponse(response);
        }
    }
}
