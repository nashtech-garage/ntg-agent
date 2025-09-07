using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace NTG.Agent.Orchestrator.Services
{
    public class GoogleTextSearchService : ITextSearchService
    {
#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        private readonly GoogleTextSearch _googleTextSearch;
#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        public GoogleTextSearchService(IConfiguration configuration)
        {
            var googleApiKey = !string.IsNullOrWhiteSpace(configuration["Google:ApiKey"])
                ? configuration["Google:ApiKey"]!
                : throw new ArgumentNullException("Google:ApiKey");

            var googleCseId = !string.IsNullOrWhiteSpace(configuration["Google:SearchEngineId"])
                ? configuration["Google:SearchEngineId"]!
                : throw new ArgumentNullException("Google:SearchEngineId");

#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            _googleTextSearch = new GoogleTextSearch(
                initializer: new() { ApiKey = googleApiKey },
                searchEngineId: googleCseId
            );
#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        }

        public async IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top = 5)
        {
#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var results = await _googleTextSearch.GetTextSearchResultsAsync(query, new() { Top = top });
#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            await foreach (var result in results.Results)
            {
                yield return result;
            }
        }
    }
}
