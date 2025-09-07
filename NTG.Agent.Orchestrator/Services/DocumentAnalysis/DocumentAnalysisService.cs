using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;
using NTG.Agent.Shared.Dtos.Upload;

namespace NTG.Agent.Orchestrator.Knowledge;

public class DocumentAnalysisService : IDocumentAnalysisService
{
    private readonly DocumentAnalysisClient _documentAnalysisClient;

    public DocumentAnalysisService(IConfiguration configuration)
    {
        var endpoint = configuration["Azure:DocumentIntelligence:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentNullException(nameof(endpoint), "Azure:DocumentIntelligence:Endpoint is required but missing or empty.");
        }

        var apiKey = configuration["Azure:DocumentIntelligence:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "Azure:DocumentIntelligence:ApiKey is required but missing or empty.");
        }

        _documentAnalysisClient = new DocumentAnalysisClient(
            new Uri(endpoint),
            new Azure.AzureKeyCredential(apiKey)
        );
    }

    public async Task<List<string>> ExtractDocumentData(
        IEnumerable<UploadItemContent> uploadItemContents)
    {
        if (_documentAnalysisClient is null || uploadItemContents == null || !uploadItemContents.Any())
            return new List<string>();

        var documentsData = new List<string>();

        foreach (var item in uploadItemContents)
        {
            try
            {
                using var stream = new MemoryStream(item.Content);
                var operation = await _documentAnalysisClient!.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                 stream
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
            catch (Exception ex)
            {
                documentsData.Add($"[Document] Analysis failed: {ex.Message}");
            }
        }

        return documentsData;
    }
}
