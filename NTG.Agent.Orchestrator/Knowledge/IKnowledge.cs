namespace NTG.Agent.Orchestrator.Knowledge;

public interface IKnowledge
{
    public Task<string> SearchKnowledgeAsync(string query, Guid agentId, CancellationToken cancellationToken = default);

    public Task<string> SearchKnowledgeAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default);

    public Task ImportDocument(Stream content, Guid agentId);
}
