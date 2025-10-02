using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Orchestrator.Services.WebSearch;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins
{
    public sealed class WebSearchPlugin
    {
        private readonly ITextSearchService _textSearchService;

        private readonly IKnowledgeService _knowledgeService;

        private readonly Guid _conversationId;

        public WebSearchPlugin(
            ITextSearchService textSearchService,
            IKnowledgeService knowledgeService,
            Guid conversationId)
        {
            _textSearchService = textSearchService;
            _conversationId = conversationId;
            _knowledgeService = knowledgeService;
        }

        [KernelFunction, Description("Search Online Web")]
        public async Task<SearchResult> SearchAsync(string query, int top = 3)
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
                        await _knowledgeService.ImportWebPageAsync(
                            url: result.Link,
                            conversationId: _conversationId
                        );
                    }
                    catch
                    {
                        // ignore failures
                    }
                });

            await Task.WhenAll(importTasks);

            // 3️. Retrieve ingested content per conversation
            var searchResult = await _knowledgeService.SearchPerConversationAsync(query, _conversationId);

            return searchResult;
        }


    }
}
