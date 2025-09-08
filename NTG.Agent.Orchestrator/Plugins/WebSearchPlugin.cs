using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Orchestrator.Services.WebSearch;
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
            Guid conversationId) // Pass conversationId here
        {
            _textSearchService = textSearchService;
            _conversationId = conversationId;
            _knowledgeService = knowledgeService;
            _kernel = kernel;
        }

        [KernelFunction, Description("Search Online Web")]
        public async Task<string> SearchAsync(string query, int top = 5)
        {
            var sourceUrls = new List<(string Title, string Url)>();

            // Ingest new web search results
            await foreach (var result in _textSearchService.SearchAsync(query, top))
            {
                if (!string.IsNullOrEmpty(result.Link))
                {
                    try
                    {
                        await _knowledgeService.ImportWebPageAsync(
                            url: result.Link,
                            conversationId: _conversationId
                        );

                        // Use the result title if available, otherwise fallback to the URL itself
                        var title = string.IsNullOrEmpty(result.Name) ? result.Link : result.Name;
                        sourceUrls.Add((title, result.Link));
                    }
                    catch
                    {
                        continue; // ignore failures
                    }
                }
            }

            // Retrieve ingested content per conversation
            var searchResult = await _knowledgeService.SearchPerConversationAsync(query, _conversationId);

            // Combine all chunks from Results
            var sbContent = new StringBuilder();
            if (searchResult.Results != null)
            {
                foreach (var citation in searchResult.Results)
                {
                    foreach (var partition in citation.Partitions)
                    {
                        var content = CleanText(partition.Text);

                        if (content.Length > 4000) content = content.Substring(0, 4000) + "...";

                        sbContent.AppendLine(content);
                    }
                }
            }

            // Summarize the combined content
            var summarizer = new ChatCompletionAgent
            {
                Name = "ConversationSummarizer",
                Instructions = "Summarize the following content into a concise paragraph that captures key points.",
                Kernel = _kernel
            };

            var sb = new StringBuilder();
            await foreach (var res in summarizer.InvokeAsync(sbContent.ToString()))
                sb.Append(res.Message);

            // Append source URLs as Markdown hyperlinks
            if (sourceUrls.Any())
            {
                sb.AppendLine("\nSources:");
                foreach (var (title, url) in sourceUrls.Distinct())
                {
                    sb.AppendLine($"- [{title}]({url})");
                }
            }

            return sb.ToString();
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
