using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Services.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class KnowledgePlugin(IKnowledgeService knowledgeService, List<string> tags)
{
    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly List<string> _tags = tags ?? throw new ArgumentNullException(nameof(tags));

    [KernelFunction, Description("intelligently query knowledge base using search or ask based on query type")]
    public async Task<string> QueryAsync(string query)
    {
        bool useAsk = ShouldUseAsk(query);
        
        if (useAsk)
        {
            var answer = await _knowledgeService.AskAsync(query, Guid.Empty, _tags);
            return FormatAnswer(answer);
        }
        else
        {
            var searchResult = await _knowledgeService.SearchAsync(query, Guid.Empty, _tags);
            return FormatSearchResults(searchResult);
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
        
        // Default to Ask for conversational queries
        return true;
    }

    private static string FormatAnswer(MemoryAnswer answer)
    {
        var result = answer.Result;
        
        if (answer.RelevantSources?.Count > 0)
        {
            result += "\n\nSources:\n";
            result += string.Join("\n", answer.RelevantSources.Select(s => $"- {s.SourceName}"));
        }
        
        return result;
    }

    private static string FormatSearchResults(SearchResult searchResult)
    {
        if (searchResult.Results?.Count == 0) return "No relevant documents found.";
        
        var results = searchResult.Results?.Take(3);
        return string.Join("\n\n", results!.Select(r => 
            $"**{r.SourceName}**\n{r.Partitions.FirstOrDefault()?.Text ?? "No content available"}\n(Relevance: {r.Partitions.FirstOrDefault()?.Relevance:F2})"));
    }
}
