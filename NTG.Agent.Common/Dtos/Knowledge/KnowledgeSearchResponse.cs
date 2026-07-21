namespace NTG.Agent.Common.Dtos.Knowledge;

public record KnowledgeSearchResponse(bool IsEmpty, string Query, List<KnowledgeSearchMatch> Results);
public record KnowledgeSearchMatch(string SourceName, string Text, double Relevance);
