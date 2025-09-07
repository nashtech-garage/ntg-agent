using Microsoft.SemanticKernel.Data;

namespace NTG.Agent.Orchestrator.Services
{
    public interface ITextSearchService
    {
        IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top = 5);
    }
}
