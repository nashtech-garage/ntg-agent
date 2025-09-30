using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Orchestrator.Services.WebSearch;
using System;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NTG.Agent.Orchestrator.Plugins
{
    public sealed class WebSearchPlugin
    {
        private readonly ITextSearchService _textSearchService;

        private readonly IKnowledgeService _knowledgeService;

        private readonly Guid _conversationId;

        private readonly Kernel _kernel;

        public WebSearchPlugin(
            ITextSearchService textSearchService,
            IKnowledgeService knowledgeService,
            Kernel kernel,
            Guid conversationId)
        {
            _textSearchService = textSearchService;
            _conversationId = conversationId;
            _knowledgeService = knowledgeService;
            _kernel = kernel;
        }

        [KernelFunction, Description("Search Online Web")]
        public async Task<SearchResult> SearchAsync(string query, int top = 5)
        {
            // Ingest new web search results
            await foreach (var result in _textSearchService.SearchAsync(query, top))
            {
                if (!string.IsNullOrEmpty(result.Link))
                {
                    try
                    {
                        await _knowledgeService.ImportWebPageAsync(
                            sourceUrl: result.Link,
                            conversationId: _conversationId
                        );
                    }
                    catch
                    {
                        continue; // ignore failures
                    }
                }
            }

            // Retrieve ingested content per conversation
            var searchResult = await _knowledgeService.SearchPerConversationAsync(query, _conversationId);

            return searchResult;
        }
    }
}
