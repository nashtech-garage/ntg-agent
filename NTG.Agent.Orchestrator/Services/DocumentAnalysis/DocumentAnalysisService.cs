using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Options;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.DocumentAnalysis;

public class DocumentAnalysisService : IDocumentAnalysisService
{
    private readonly DocumentAnalysisClient? _documentAnalysisClient;
    private readonly ILogger<DocumentAnalysisService> _logger;

    /// <inheritdoc />
    public bool IsEnabled { get; }

    public DocumentAnalysisService(IOptions<DocumentIntelligenceSettings> options, ILogger<DocumentAnalysisService> logger)
    {
        _logger = logger;
        var settings = options.Value;
        IsEnabled = settings.IsEnabled;

        if (!IsEnabled)
        {
            // Document Intelligence is disabled — the service is a no-op.
            // File upload buttons will be hidden on the client via the /api/features endpoint.
            _logger.LogInformation("Azure Document Intelligence is disabled. File upload in chat will not be available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Endpoint))
            throw new InvalidOperationException("Azure:DocumentIntelligence:Endpoint is required when IsEnabled is true.");

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Azure:DocumentIntelligence:ApiKey is required when IsEnabled is true.");

        _documentAnalysisClient = new DocumentAnalysisClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey));
    }

    public async Task<List<string>> ExtractDocumentData(
        IEnumerable<UploadItemForm> uploadItemContents)
    {
        // Return early when the feature is disabled or there is nothing to process.
        if (!IsEnabled || _documentAnalysisClient is null || uploadItemContents == null || !uploadItemContents.Any())
            return new List<string>();

        var documentsData = new List<string>();

        foreach (var item in uploadItemContents)
        {
            try
            {
                if (item.Content is IFormFile file)
                {
                    var operation = await _documentAnalysisClient!.AnalyzeDocumentAsync(
                            WaitUntil.Completed,
                            "prebuilt-read",
                             file.OpenReadStream()
                            );

                    var result = operation.Value;

                    // Extract text paragraphs
                    var paragraphs = result.Paragraphs?
                        .Select(p => p.Content)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .ToList() ?? new List<string>();

                    var docString = $@"
                    [Document]
                    Text:
                    {string.Join(Environment.NewLine, paragraphs)}
                    ";
                    documentsData.Add(docString);
                }

            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Document analysis failed for an upload item.");
                documentsData.Add($"[Document] Analysis failed: {ex.Message}");
            }
        }

        return documentsData;
    }
}