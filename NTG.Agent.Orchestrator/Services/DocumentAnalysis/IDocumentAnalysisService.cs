using NTG.Agent.Orchestrator.Dtos;

namespace NTG.Agent.Orchestrator.Services.DocumentAnalysis;

public interface IDocumentAnalysisService
{
    Task<List<string>> ExtractDocumentData(
        IEnumerable<UploadItemForm> uploadItemContents);
}
