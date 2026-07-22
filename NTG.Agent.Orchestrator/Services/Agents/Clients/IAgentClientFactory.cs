using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

public interface IAgentClientFactory
{
    IChatClient CreateChatClient(Models.Agents.Agent agent, bool enableThinking);
}
