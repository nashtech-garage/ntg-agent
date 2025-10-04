using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.Services.WebSearch;
using NTG.Agent.Shared.Services.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.MCP.Server.McpTools
{
    [McpServerToolType]
    public sealed class WebSearchTool
    {
        private readonly ITextSearchService _textSearchService;

        private readonly IKnowledgeScraperService _knowledgeScraperService;

        public WebSearchTool(
            ITextSearchService textSearchService,
            IKnowledgeScraperService knowledgeScraperService)
        {
            _textSearchService = textSearchService;
            _knowledgeScraperService = knowledgeScraperService;
        }

        [McpServerTool, Description("Search Online Web")]
        public async Task<SearchResult> SearchOnlineAsync(
        [Description("Search query text")] string query,
        [Description("Conversation ID for scoping search results")] Guid conversationId,
        [Description("Maximum number of online search results to fetch")] int top = 3)
        {
            // 1️. Get search results
            var results = await _textSearchService.SearchAsync(query, top)
                                                  .ToListAsync();

            // 2️. Import pages in parallel
            var importTasks = results
                .Where(r => !string.IsNullOrEmpty(r.Link))
                .Select(async result =>
                {
                    try
                    {
                        await _knowledgeScraperService.ImportWebPageAsync(
                            url: result.Link,
                            conversationId: conversationId
                        );
                    }
                    catch
                    {
                        // ignore failures
                    }
                });

            await Task.WhenAll(importTasks);

            // 3️. Retrieve ingested content per conversation
            var searchResult = await _knowledgeScraperService.SearchPerConversationAsync(query, conversationId);

            return searchResult;
        }
    }
}
