using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Services.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class KnowledgePlugin(IKnowledgeService knowledgeService, List<string> tags)
{
    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly List<string> _tags = tags ?? throw new ArgumentNullException(nameof(tags));

    [KernelFunction, Description("query knowledge base - pass the COMPLETE user request unchanged including words like 'search', 'find', 'list'")]
    public async Task<KnowledgeResult> QueryAsync(string query)
    {
        bool useAsk = ShouldUseAsk(query);
        
        if (useAsk)
        {
            var answer = await _knowledgeService.AskAsync(query, Guid.Empty, _tags);
            return new KnowledgeResult { Answer = answer };
        }
        else
        {
            var result = await _knowledgeService.SearchAsync(query, Guid.Empty, _tags);
            return new KnowledgeResult { SearchResult = result };
        }
    }

    private static bool ShouldUseAsk(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        
        // Use Ask for question words and direct inquiries
        string[] questionWords = ["what", "how", "why", "when", "where", "who", "can", "should", "would", "could", "is", "are", "does", "do", "explain", "describe", "tell me"];
        
        // Use Search for finding/listing tasks
        string[] searchWords = ["find", "show", "list", "search", "get", "retrieve", "locate", "lookup"];
        
        bool hasQuestionWords = questionWords.Any(lowerQuery.Contains);
        bool hasSearchWords = searchWords.Any(lowerQuery.Contains);
        
        if (hasQuestionWords && !hasSearchWords) return true;
        
        if (hasSearchWords) return false;
        
        if (lowerQuery.EndsWith('?')) return true;
        
        // Default to Search for better reliability
        return false;
    }
}

public class KnowledgeResult
{
    public MemoryAnswer? Answer { get; set; }
    public SearchResult? SearchResult { get; set; }
    public bool IsAnswer => Answer != null;
}
