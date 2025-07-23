using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class KnowledgePlugin
{
    private readonly IKnowledgeService _knowledgeService;
    public KnowledgePlugin(IKnowledgeService knowledgeService)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    }

    [KernelFunction, Description("search knowledge base")]
    public async Task<SearchResult> SearchAsync(string query)
    {
        var result =  await _knowledgeService.SearchAsync(query, Guid.Empty);
        return result;
    }
}
