using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Agents;
public interface IAgentFactory
{
    string ToolContext { get; set; }

    Task<AIAgent> CreateAgent(Guid agentId);
    Task<AIAgent> CreateBasicAgent(string instructions);
    Task<List<AITool>> GetAvailableTools(Models.Agents.Agent agent);
    Task<IEnumerable<AITool>> GetMcpToolsAsync(string endpoint);
}