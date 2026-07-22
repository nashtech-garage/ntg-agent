namespace NTG.Agent.LightRag;

/// <summary>
/// Persistence seam for the identity-bound agent-port reservations (see
/// <see cref="PortReservationService"/>). The host application implements this against its
/// own data store so the LightRAG provider stays free of any EF/DbContext dependency.
/// Implementations are resolved from a scoped service provider.
/// </summary>
public interface ILightRagAgentPortStore
{
    /// <summary>All agent ids known to the host (used by the startup reconciler).</summary>
    Task<IReadOnlyList<Guid>> GetAgentIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>The agent's reserved host port, or null when none is assigned yet.</summary>
    Task<int?> GetPortAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>Agents that currently hold a port reservation (used by idle shutdown).</summary>
    Task<IReadOnlyList<(Guid AgentId, int Port)>> GetAssignedPortsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ports reserved by any agent other than <paramref name="excludeAgentId"/>.</summary>
    Task<IReadOnlyList<int>> GetReservedPortsAsync(Guid excludeAgentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the agent's port reservation. Throws <see cref="InvalidOperationException"/>
    /// when the agent does not exist.
    /// </summary>
    Task SetPortAsync(Guid agentId, int port, CancellationToken cancellationToken = default);
}
