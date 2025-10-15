using Microsoft.SemanticKernel.Data;
using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.Dtos;
using NTG.Agent.MCP.Server.Extensions;
using NTG.Agent.MCP.Server.Services.WebSearch;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.McpTools
{
    [McpServerToolType]
    public sealed class WebSearchTool
    {
        private readonly ITextSearchService _textSearchService;

        private readonly IWebScraper _webScraper;

        public WebSearchTool(
            ITextSearchService textSearchService,
            IWebScraper webScraper )
        {
            _textSearchService = textSearchService;
            _webScraper = webScraper;
        }

        [McpServerTool, Description("Search Online Web")]
        public async Task<string> SearchOnlineAsync(
        [Description("the value to search")] string query,
        [Description("Maximum number of online search results to fetch")] int top = 3)
        {
            var results = new List<WebSearchResult>();

            // 1️. Get search results
            var textSearchResults = new List<TextSearchResult>();
            await foreach (var item in _textSearchService.SearchAsync(query, top))
            {
                textSearchResults.Add(item);
            };

            // 2️. Import pages in parallel
            var importTasks = textSearchResults
                .Where(r => !string.IsNullOrEmpty(r.Link))
                .Select(async result =>
                {
                    try
                    {
                        var webPage = await _webScraper.GetContentAsync(result.Link);
                        var htmlContent = webPage.Content.ToString();
                        var cleanedHtml = htmlContent.CleanHtml();
                        results.Add(new WebSearchResult
                        {
                            Url = result.Link,
                            Content = cleanedHtml
                        });
                    }
                    catch
                    {
                        // ignore failures
                    }
                });

            await Task.WhenAll(importTasks);
            var serializedResult = JsonSerializer.Serialize(results);
            return serializedResult;
        }
    }
}
