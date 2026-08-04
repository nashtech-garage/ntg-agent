namespace NTG.Agent.LightRag;

/// <summary>
/// Persistence seam listing the agents the LightRAG provider manages containers for. The
/// host application implements this against its own data store so the LightRAG provider
/// stays free of any EF/DbContext dependency. Implementations are resolved from a scoped
/// service provider.
/// </summary>
public interface ILightRagAgentStore
{
    /// <summary>All agent ids known to the host (used by the startup reconciler).</summary>
    Task<IReadOnlyList<Guid>> GetAgentIdsAsync(CancellationToken cancellationToken = default);
}
