using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Skills;

namespace NTG.Agent.Orchestrator.Services.Agents;
public interface IAgentFactory
{
    string ToolContext { get; set; }

    Task<AIAgent> CreateAgent(Guid agentId);
    Task<AIAgent> CreateBasicAgent(string instructions);
    Task<List<AITool>> GetAvailableTools(Models.Agents.Agent agent);
    Task<IEnumerable<AITool>> GetMcpToolsAsync(string endpoint);
    Task<IReadOnlyList<AgentSkillListItemDto>> GetMcpSkillsAsync(string endpoint);
}