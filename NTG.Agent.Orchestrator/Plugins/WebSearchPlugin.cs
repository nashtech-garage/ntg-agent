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
            // 1️⃣ Get search results
            var results = await _textSearchService.SearchAsync(query, top)
                                                  .ToListAsync(); // materialize the async enumerable

            // 2️⃣ Import pages in parallel
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

            // 3️⃣ Retrieve ingested content per conversation
            var searchResult = await _knowledgeService.SearchPerConversationAsync(query, _conversationId);

            // 4️⃣ Clean and truncate content
            if (searchResult.Results != null)
            {
                foreach (var citation in searchResult.Results)
                {
                    foreach (var partition in citation.Partitions)
                    {
                        var content = CleanText(partition.Text);
                        if (content.Length > 4000)
                            content = content.Substring(0, 4000) + "...";
                        partition.Text = content;
                    }
                }
            }

            return searchResult;
        }


        private string CleanText(string input)
        {
            // Decode HTML entities
            var text = WebUtility.HtmlDecode(input);

            // Strip HTML tags
            text = Regex.Replace(text, "<.*?>", string.Empty);

            // Replace multiple whitespaces/newlines/tabs with a single space
            text = Regex.Replace(text, @"\s+", " ");

            // Trim leading/trailing spaces
            return text.Trim();
        }
    }
}
