using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Services.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class KnowledgePlugin
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly Guid _conversationId;
    private readonly List<string> _tags;
    public KnowledgePlugin(IKnowledgeService knowledgeService, List<string> tags, Guid conversationId)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _tags = tags ?? throw new ArgumentNullException(nameof(tags));
        _conversationId = conversationId;
    }

    [KernelFunction, Description("search knowledge base")]
    public async Task<SearchResult> SearchAsync(string query)
    {
        var result =  await _knowledgeService.SearchAsync(query, Guid.Empty, _tags);

        if(result.NoResult)
        {
            // Fallback to search per conversation if no results found with global knowledge base search
            result = await _knowledgeService.SearchPerConversationAsync(query, _conversationId);
        }

        return result;
    }
}
