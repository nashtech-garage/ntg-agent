using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.ViewModels;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AgentsController : ControllerBase
{
    private readonly IAgentService _agentService;

    public AgentsController(IAgentService agentService)
    {
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
    }

    [HttpPost("chat")]
    public async IAsyncEnumerable<PromptResponse> ChatAsync([FromBody] PromptRequest promptRequest)
    {
        Guid? userId = User.GetUserId();

        await foreach (var response in _agentService.InvokePromptStreamingAsync(userId, promptRequest.ConversationId, promptRequest.Prompt))
        {
            yield return new PromptResponse(response);
        }
    }
}
