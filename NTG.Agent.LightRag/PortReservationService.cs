using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Assigns each agent a permanently-owned host port from the configured range
/// [<see cref="LightRagSettings.PortRangeStart"/>, <see cref="LightRagSettings.PortRangeEnd"/>].
/// A port is never handed to a second agent, so a reserved port uniquely identifies its
/// agent's LightRAG container — this is what makes <see cref="LightRagClientFactory"/>'s
/// cached-port fast path safe against cross-agent misrouting after idle-shutdown/recreate.
/// </summary>
public sealed class PortReservationService
{
    // Serialise allocation across request scopes so two first-time reservations can't
    // read the same "taken" set and pick the same free port. Static because the service
    // is scoped (a new instance per request) but there is one Orchestrator process.
    private static readonly SemaphoreSlim AllocationGate = new(1, 1);

    private readonly ILightRagAgentPortStore _portStore;
    private readonly LightRagSettings _settings;

    public PortReservationService(ILightRagAgentPortStore portStore, IOptions<LightRagSettings> settings)
    {
        _portStore = portStore;
        _settings = settings.Value;
    }

    /// <summary>
    /// Returns the agent's existing reserved port, or reserves the lowest free port in
    /// range (one not held by any other agent), persists it, and returns it.
    /// </summary>
    public async Task<int> GetOrReserveAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await AllocationGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _portStore.GetPortAsync(agentId, cancellationToken);
            if (existing is > 0)
                return existing.Value;

            var port = await AllocateFreePortAsync(excludeAgentId: agentId, avoidPort: null, cancellationToken);
            await _portStore.SetPortAsync(agentId, port, cancellationToken);
            return port;
        }
        finally
        {
            AllocationGate.Release();
        }
    }

    /// <summary>
    /// Assigns the agent a different free port (used when its current reservation is held
    /// on the host by an external process), persists it, and returns it.
    /// </summary>
    public async Task<int> ReassignAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await AllocationGate.WaitAsync(cancellationToken);
        try
        {
            var current = await _portStore.GetPortAsync(agentId, cancellationToken);
            var port = await AllocateFreePortAsync(excludeAgentId: agentId, avoidPort: current, cancellationToken);
            await _portStore.SetPortAsync(agentId, port, cancellationToken);
            return port;
        }
        finally
        {
            AllocationGate.Release();
        }
    }

    // Lowest port in range not reserved by any *other* agent (and not the avoided one).
    private async Task<int> AllocateFreePortAsync(Guid excludeAgentId, int? avoidPort, CancellationToken cancellationToken)
    {
        var taken = (await _portStore.GetReservedPortsAsync(excludeAgentId, cancellationToken)).ToHashSet();

        for (var port = _settings.PortRangeStart; port <= _settings.PortRangeEnd; port++)
        {
            if (port == avoidPort)
                continue;
            if (!taken.Contains(port))
                return port;
        }

        throw new PortPoolExhaustedException(_settings.PortRangeStart, _settings.PortRangeEnd);
    }
}
