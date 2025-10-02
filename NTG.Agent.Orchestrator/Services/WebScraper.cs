using Microsoft.KernelMemory.DataFormats.WebPages;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;
using NTG.Agent.Orchestrator.Extentions;
using Polly;
using System.Net;
using System.Net.Mime;

namespace NTG.Agent.Orchestrator.Services;

public sealed class WebScraper : IWebScraper, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _log;

    public WebScraper(
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Telemetry.HttpUserAgent);

        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<WebScraper>();
    }

    /// <inheritdoc />
    public async Task<WebScraperResult> GetContentAsync(string url, CancellationToken cancellationToken = default)
    {
        return await GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<WebScraperResult> GetAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var scheme = url.Scheme.ToUpperInvariant();
        if (scheme is not "HTTP" and not "HTTPS")
        {
            return new WebScraperResult { Success = false, Error = $"Unknown URL protocol: {url.Scheme}" };
        }

        HttpResponseMessage? response = await RetryLogic()
            .ExecuteAsync(async _ => await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _log.LogError("Error while fetching page {0}, status code: {1}", url.AbsoluteUri, response.StatusCode);
            return new WebScraperResult { Success = false, Error = $"HTTP error, status code: {response.StatusCode}" };
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (string.IsNullOrEmpty(contentType))
        {
            return new WebScraperResult { Success = false, Error = "No content type available" };
        }

        contentType = FixContentType(contentType, url);
        _log.LogDebug("URL '{0}' fetched, content type: {1}", url.AbsoluteUri, contentType);

        var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        // Read all bytes to avoid System.InvalidOperationException exception "Timeouts are not supported on this stream"
        var bytes = content.ReadAllBytes();
        return new WebScraperResult
        {
            Success = true,
            Content = new BinaryData(bytes),
            ContentType = contentType
        };
    }

    private static string FixContentType(string contentType, Uri url)
    {
        // Change type to Markdown if necessary. Most web servers, e.g. GitHub, return "text/plain" also for markdown files
        if (contentType.Contains(MimeTypes.PlainText, StringComparison.OrdinalIgnoreCase)
            && url.AbsolutePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return MimeTypes.MarkDown;
        }

        // Use new Markdown type
        if (contentType.Contains(MimeTypes.MarkDownOld1, StringComparison.OrdinalIgnoreCase)
            || contentType.Contains(MimeTypes.MarkDownOld2, StringComparison.OrdinalIgnoreCase))
        {
            return MimeTypes.MarkDown;
        }

        // Use proper XML type
        if (contentType.Contains(MimeTypes.XML2, StringComparison.OrdinalIgnoreCase))
        {
            return MimeTypes.XML;
        }

        // Return only the first part, e.g. leaving out encoding
        return new ContentType(contentType).MediaType;
    }

    private static ResiliencePipeline<HttpResponseMessage> RetryLogic()
    {
        var retriableErrors = new[]
        {
            HttpStatusCode.RequestTimeout, // 408
            HttpStatusCode.InternalServerError, // 500
            HttpStatusCode.BadGateway, // 502
            HttpStatusCode.GatewayTimeout, // 504
        };

        const int MaxDelay = 5;
        var delays = new List<int> { 1, 1, 1, 2, 2, 3, 4, MaxDelay };

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(resp => retriableErrors.Contains(resp.StatusCode)),
                MaxRetryAttempts = 10,
                DelayGenerator = args =>
                {
                    double secs = args.AttemptNumber < delays.Count ? delays[args.AttemptNumber] : MaxDelay;
                    return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromSeconds(secs));
                }
            })
            .Build();
    }
}
