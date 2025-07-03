using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.ViewModels;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AgentsController : ControllerBase
{
  private readonly IAgentService _agentService;
  private readonly IChatHistoryService _chatHistoryService;

  public AgentsController(IAgentService agentService, IChatHistoryService chatHistoryService)
  {
    _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
    _chatHistoryService = chatHistoryService ?? throw new ArgumentNullException(nameof(chatHistoryService));
  }

  [HttpPost("chat")]
  public async IAsyncEnumerable<PromptResponse> ChatAsync([FromBody] PromptRequest promptRequest)
  {
    var newConversationId = await _chatHistoryService.CreateNewConversationAsync(Guid.NewGuid().ToString());

    _chatHistoryService.AddSystemMessage("You are a helpful assistant");

    _chatHistoryService.AddUserMessage(promptRequest.Prompt);
    // FIXME: This is saving repeating messages
    // Better be two entities: Conversation/Thread and Message
    await foreach (var response in _agentService.InvokePromptStreamingAsync(promptRequest.Prompt))
    {
      _chatHistoryService.AddAssistantMessage(response);
      yield return new PromptResponse(response);
    }

    await _chatHistoryService.SaveAsync(newConversationId);
  }
}
