namespace NTG.Agent.Common.Dtos.Documents;

public record UploadDocumentResult(string FileName, bool Success, string? ErrorMessage, Guid? DocumentId);
