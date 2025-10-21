using Microsoft.SemanticKernel.Data;

namespace NTG.Agent.AITools.SearchOnlineTool.Services;

public interface ITextSearchService
{
    IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top);
}
