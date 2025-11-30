namespace NTG.Agent.Common.Dtos.Documents;

/// <summary>
/// Request to update tags associated with a document
/// </summary>
/// <param name="TagIds">List of tag IDs to assign to the document</param>
public record UpdateDocumentTagsRequest(List<string> TagIds);
