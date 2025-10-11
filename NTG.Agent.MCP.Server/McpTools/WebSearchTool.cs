using Microsoft.SemanticKernel.Data;
using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.Services.WebSearch;
using System.ComponentModel;

namespace NTG.Agent.MCP.Server.McpTools
{
    [McpServerToolType]
    public sealed class WebSearchTool
    {
        private readonly ITextSearchService _textSearchService;

        public WebSearchTool(
            ITextSearchService textSearchService)
        {
            _textSearchService = textSearchService;
        }

        [McpServerTool, Description("Search Online Web")]
        public async Task<List<TextSearchResult>> SearchOnlineAsync(
        [Description("Search query text")] string query,
        [Description("Maximum number of online search results to fetch")] int top = 3)
        {
            var results = new List<TextSearchResult>();

            await foreach (var item in _textSearchService.SearchAsync(query, top))
            {
                results.Add(item);
            }

            return results;
        }
    }
}
