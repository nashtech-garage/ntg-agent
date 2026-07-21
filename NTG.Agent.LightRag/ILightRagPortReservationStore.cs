namespace NTG.Agent.LightRag;

/// <summary>
/// The <b>global</b> authority for LightRAG host-port reservations, shared by every developer's
/// Orchestrator (backed by the <c>agent_port_reservations</c> table in the shared Postgres).
/// <para>
/// This exists because the port pool belongs to the shared Docker host, while each developer runs
/// their own local application database. Allocating from a local DB could only see that developer's
/// agents, so two developers independently picked the same port and collided on container start.
/// Reserving through one table — arbitrated by a UNIQUE constraint on the port — makes that
/// impossible team-wide.
/// </para>
/// <para>
/// Distinct from <see cref="ILightRagAgentPortStore"/>, which is the host application's <i>local</i>
/// store: it caches the resolved port on the agent row so the chat hot path never needs a cross-database
/// round-trip. This interface is the source of truth; that one is a cache.
/// </para>
/// </summary>
public interface ILightRagPortReservationStore
{
    /// <summary>The agent's reserved port, or null when it holds no reservation.</summary>
    Task<int?> GetReservedPortAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the agent's existing reservation, or atomically reserves the lowest free port in
    /// [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>] and returns it. Safe against
    /// concurrent reservations from other developers' Orchestrators.
    /// </summary>
    /// <exception cref="PortPoolExhaustedException">No free port remains in the range.</exception>
    Task<int> GetOrReserveAsync(Guid agentId, int rangeStart, int rangeEnd, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the agent to a different free port (used when its current one is held on the host by an
    /// external process), releasing the old one back to the pool.
    /// </summary>
    /// <exception cref="PortPoolExhaustedException">No free port remains in the range.</exception>
    Task<int> ReassignAsync(Guid agentId, int rangeStart, int rangeEnd, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the agent's reservation so the port returns to the pool. Called when an agent is
    /// deleted (its container is torn down, so the port becomes reusable by a new agent).
    /// </summary>
    Task ReleaseAsync(Guid agentId, CancellationToken cancellationToken = default);
}
