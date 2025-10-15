using Microsoft.Extensions.AI;
using Microsoft.KernelMemory;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Shared.Services.Knowledge;
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

    [Description("Search knowledge base")]
    public async Task<SearchResult> SearchAsync([Description("the value to search")]string query, [Description("the id of current conversation")] Guid conversationId)
    {
        var result =  await _knowledgeService.SearchAsync(query, Guid.Empty, _tags);

        if(result.NoResult)
        {
            // Fallback to search per conversation if no results found with global knowledge base search
            result = await _knowledgeService.SearchPerConversationAsync(query, conversationId);
        }

        return result;
    }

    public AITool AsAITool()
    {
        return AIFunctionFactory.Create(this.SearchAsync, new AIFunctionFactoryOptions { Name = "memory"});
    }
}
