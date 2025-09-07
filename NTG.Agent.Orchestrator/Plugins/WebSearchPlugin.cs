using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using NTG.Agent.Orchestrator.Services;

namespace NTG.Agent.Orchestrator.Plugins
{
    public sealed class WebSearchPlugin
    {
        private readonly ITextSearchService _textSearchService;

        public WebSearchPlugin(ITextSearchService textSearchService)
        {
            _textSearchService = textSearchService;
        }

        [KernelFunction("Search Web")]
        public async IAsyncEnumerable<TextSearchResult> SearchAsync(string query)
        {
            await foreach (var result in _textSearchService.SearchAsync(query))
            {
                yield return result;
            }
        }
    }
}
