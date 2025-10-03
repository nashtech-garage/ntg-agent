using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DataFormats.WebPages;
using NTG.Agent.Shared.Services.Extensions;

namespace NTG.Agent.Shared.Services.Knowledge;

public class KernelMemoryKnowledgeScraper : KernelMemoryKnowledge, IKnowledgeScraperService
{
    private readonly IWebScraper _webScraper;

    public KernelMemoryKnowledgeScraper(IKernelMemory kernelMemory,
        ILogger<KernelMemoryKnowledge> logger,
        IWebScraper webScraper) : base(kernelMemory, logger)
    {
        _webScraper = webScraper;
    }
    public async Task<string> ImportWebPageAsync(
        string url,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Invalid URL provided.", nameof(url));
        }
        var tagCollection = new TagCollection
        {
            { "conversationId", conversationId.ToString() },
            { "sourceUrl", url }
        };
        // Use the conversationId as the collection name to keep memory per conversation
        string documentId = string.Empty;
        try
        {
            var webPage = await _webScraper.GetContentAsync(url, cancellationToken);
            var htmlContent = webPage.Content.ToString();
            var cleanedHtml = htmlContent.CleanHtml();
            documentId = await _kernelMemory.ImportTextAsync(
                cleanedHtml,
                tags: tagCollection,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot import WebPage cleaned content to kernelMemory");
        }

        return documentId;
    }
}
