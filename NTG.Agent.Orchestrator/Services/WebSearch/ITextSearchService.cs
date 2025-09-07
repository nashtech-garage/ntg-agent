using Microsoft.SemanticKernel.Data;

namespace NTG.Agent.Orchestrator.Services.WebSearch
{
    public interface ITextSearchService
    {
        IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top = 5);
    }
}
