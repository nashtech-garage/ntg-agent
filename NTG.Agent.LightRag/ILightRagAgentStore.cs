namespace NTG.Agent.LightRag;

/// <summary>
/// Persistence seam mapping agents to the knowledge bases the LightRAG provider manages containers
/// for. The host application implements this against its own data store so the LightRAG provider
/// stays free of any EF/DbContext dependency. Implementations are resolved from a scoped
/// service provider.
/// <para>
/// A knowledge base is one container and one workspace, shared by every agent bound to it. It is
/// identified by the id of the agent that founded it, which is why both members below traffic in
/// agent ids rather than a separate key.
/// </para>
/// </summary>
public interface ILightRagAgentStore
{
    /// <summary>
    /// Distinct knowledge-base owner ids — one container per entry. Used by the startup reconciler,
    /// so agents that share a knowledge base cost one container between them, and agents with no
    /// knowledge base cost none.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetKnowledgeOwnerIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The knowledge-base owner backing this agent — its own id when it owns its knowledge base,
    /// otherwise the id of the agent whose knowledge base it shares. Returns <see langword="null"/>
    /// when the agent has no knowledge base at all (an inner agent, or a row that no longer exists).
    /// </summary>
    Task<Guid?> GetKnowledgeOwnerAsync(Guid agentId, CancellationToken cancellationToken = default);
}
