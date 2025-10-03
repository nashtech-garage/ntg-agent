using Microsoft.SemanticKernel.Data;

namespace NTG.Agent.MCP.Server.Services.WebSearch
{
    public interface ITextSearchService
    {
        IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top);
    }
}
