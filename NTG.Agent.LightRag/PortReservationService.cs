using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Resolves the host port an agent's LightRAG container is published on.
/// <para>
/// Allocation is delegated to <see cref="ILightRagPortReservationStore"/> — the <b>global</b> ledger in
/// the shared Postgres — because the port pool belongs to the shared Docker host, not to any one
/// developer's database. That is what stops two developers from independently handing out the same
/// port and colliding on container start.
/// </para>
/// <para>
/// A port is identity-bound: it stays with its agent until the agent is deleted, at which point
/// <see cref="ReleaseAsync"/> returns it to the pool. No in-process lock is needed — uniqueness is
/// enforced by the database, which is the only thing all developers share.
/// </para>
/// </summary>
public sealed class PortReservationService
{
    private readonly ILightRagAgentPortStore _portCache;
    private readonly ILightRagPortReservationStore _reservations;
    private readonly LightRagSettings _settings;

    public PortReservationService(
        ILightRagAgentPortStore portCache,
        ILightRagPortReservationStore reservations,
        IOptions<LightRagSettings> settings)
    {
        _portCache = portCache;
        _reservations = reservations;
        _settings = settings.Value;
    }

    /// <summary>
    /// Returns the agent's reserved port, reserving one from the global ledger if it has none.
    /// </summary>
    public async Task<int> GetOrReserveAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        // Fast path: the local cache already knows this agent's identity-bound port.
        var cached = await _portCache.GetPortAsync(agentId, cancellationToken);
        if (cached is > 0)
            return cached.Value;

        var port = await _reservations.GetOrReserveAsync(
            agentId, _settings.PortRangeStart, _settings.PortRangeEnd, cancellationToken);

        await _portCache.SetPortAsync(agentId, port, cancellationToken);
        return port;
    }

    /// <summary>
    /// Assigns the agent a different port (used when its current one is held on the host by an external
    /// process) and refreshes the local cache.
    /// </summary>
    public async Task<int> ReassignAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var port = await _reservations.ReassignAsync(
            agentId, _settings.PortRangeStart, _settings.PortRangeEnd, cancellationToken);

        await _portCache.SetPortAsync(agentId, port, cancellationToken);
        return port;
    }

    /// <summary>
    /// Releases the agent's reservation back to the pool. Called when an agent is deleted: its container
    /// is torn down, so the port is free for a future agent to reserve.
    /// </summary>
    public Task ReleaseAsync(Guid agentId, CancellationToken cancellationToken = default)
        => _reservations.ReleaseAsync(agentId, cancellationToken);
}
