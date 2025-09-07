using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Services.WebSearch;
using System.ComponentModel;
using System.Text;

namespace NTG.Agent.Orchestrator.Plugins
{
    public sealed class WebSearchPlugin
    {
        private readonly ITextSearchService _textSearchService;

        public WebSearchPlugin(ITextSearchService textSearchService)
        {
            _textSearchService = textSearchService;
        }

        [KernelFunction, Description("Search Online Web")]
        public async Task<string> SearchAsync(string query)
        {
            var sb = new StringBuilder();

            await foreach (var result in _textSearchService.SearchAsync(query))
            {
                sb.AppendLine($"- {result.Name ?? "No title"}");
                sb.AppendLine($"  {result.Value}");
                sb.AppendLine($"  {result.Link ?? "No URL"}");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
