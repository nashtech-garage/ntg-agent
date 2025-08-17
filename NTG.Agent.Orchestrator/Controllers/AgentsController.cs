using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.Data;
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
    public async IAsyncEnumerable<PromptResponse> ChatAsync([FromBody] PromptRequest promptRequest)
    {
        Guid? userId = User.GetUserId();
        List<string> tags = new List<string>();
        if (userId is not null)
        {
            var roleIds = await _agentDbContext.UserRoles.Where(c=>c.UserId == userId).Select(c=>c.RoleId).ToListAsync();
            tags = await _agentDbContext.TagRoles
                .Where(c => roleIds.Contains(c.RoleId))
                .Select(c => c.Tag.Name)
                .ToListAsync();
        }
        else
        {

        }
        await foreach (var response in _agentService.ChatStreamingAsync(userId, promptRequest, tags))
        {
            yield return new PromptResponse(response);
        }
    }
}
