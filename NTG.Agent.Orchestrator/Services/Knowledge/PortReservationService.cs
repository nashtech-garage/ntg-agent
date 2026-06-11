using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

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

    private readonly AgentDbContext _db;
    private readonly LightRagSettings _settings;

    public PortReservationService(AgentDbContext db, IOptions<LightRagSettings> settings)
    {
        _db = db;
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
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
                ?? throw new InvalidOperationException($"Agent {agentId} not found while reserving a LightRAG port.");

            if (agent.LightRagPort is > 0)
                return agent.LightRagPort.Value;

            var port = await AllocateFreePortAsync(excludeAgentId: agentId, avoidPort: null, cancellationToken);
            agent.LightRagPort = port;
            await _db.SaveChangesAsync(cancellationToken);
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
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
                ?? throw new InvalidOperationException($"Agent {agentId} not found while reassigning a LightRAG port.");

            var port = await AllocateFreePortAsync(excludeAgentId: agentId, avoidPort: agent.LightRagPort, cancellationToken);
            agent.LightRagPort = port;
            await _db.SaveChangesAsync(cancellationToken);
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
        var taken = (await _db.Agents
            .Where(a => a.Id != excludeAgentId && a.LightRagPort != null)
            .Select(a => a.LightRagPort!.Value)
            .ToListAsync(cancellationToken))
            .ToHashSet();

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
