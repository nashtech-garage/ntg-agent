using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class KnowledgePlugin
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly List<string> _tags;
    public KnowledgePlugin(IKnowledgeService knowledgeService, List<string> tags)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _tags = tags ?? throw new ArgumentNullException(nameof(tags));
    }

    [KernelFunction, Description("search knowledge base")]
    public async Task<SearchResult> SearchAsync(string query)
    {
        var result =  await _knowledgeService.SearchAsync(query, Guid.Empty, _tags);
        return result;
    }
}
