using NTG.Agent.Shared.Dtos.Upload;

namespace NTG.Agent.Orchestrator.Services.DocumentAnalysis;

public interface IDocumentAnalysisService
{
    Task<List<string>> ExtractDocumentData(
        IEnumerable<UploadItemContent> uploadItemContents);
}
