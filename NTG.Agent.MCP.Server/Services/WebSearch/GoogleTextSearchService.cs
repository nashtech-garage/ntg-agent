using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace NTG.Agent.MCP.Server.Services.WebSearch
{
    public class GoogleTextSearchService : ITextSearchService
    {
        private readonly ITextSearch _googleTextSearch;

        public GoogleTextSearchService(ITextSearch googleTextSearch)
        {
            _googleTextSearch = googleTextSearch ?? throw new ArgumentNullException(nameof(googleTextSearch));
        }

        public async IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top)
        {
            var results = await _googleTextSearch.GetTextSearchResultsAsync(query, new() { Top = top });

            await foreach (var result in results.Results)
            {
                yield return result;
            }
        }
    }
}
