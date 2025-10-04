namespace NTG.Agent.Shared.Services.Knowledge;

public interface IKnowledgeScraperService : IKnowledgeService
{
    public Task<string> ImportWebPageAsync(string url, Guid conversationId, CancellationToken cancellationToken = default);

}
