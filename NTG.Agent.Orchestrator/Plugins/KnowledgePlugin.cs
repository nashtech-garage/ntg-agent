using Microsoft.Extensions.AI;
using Microsoft.KernelMemory;
using NTG.Agent.Orchestrator.Services.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class KnowledgePlugin
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly List<string> _tags;
    private readonly Guid _agentId;

    public KnowledgePlugin(IKnowledgeService knowledgeService, Guid agentId, List<string> tags)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _tags = tags ?? throw new ArgumentNullException(nameof(tags));
        _agentId = agentId;
    }

    [Description("Search knowledge base")]
    public async Task<SearchResult> SearchAsync([Description("the value to search")]string query)
    {
        var result =  await _knowledgeService.SearchAsync(query, _agentId, _tags);
        return result;
    }

    public AITool AsAITool()
    {
        return AIFunctionFactory.Create(this.SearchAsync, new AIFunctionFactoryOptions { Name = "memory"});
    }
}
