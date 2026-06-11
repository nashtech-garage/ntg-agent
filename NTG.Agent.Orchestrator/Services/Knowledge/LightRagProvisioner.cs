using NTG.Agent.Orchestrator.Exceptions;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Single place that provisions an agent's LightRAG container on its identity-bound port:
/// reserve the agent's reserved port, ensure the container runs on it, and — if that port
/// is held on the host by an external process — reassign a new reserved port and retry once.
/// Used by the client factory (on a cache miss), the startup reconciler, and agent creation
/// so the reserve→ensure→reassign flow lives in exactly one place.
/// </summary>
public sealed class LightRagProvisioner : ILightRagProvisioner
{
    private readonly PortReservationService _reservation;
    private readonly ILightRagContainerManager _containerManager;

    public LightRagProvisioner(PortReservationService reservation, ILightRagContainerManager containerManager)
    {
        _reservation = reservation;
        _containerManager = containerManager;
    }

    /// <summary>
    /// Ensures the agent's container is running on its reserved port and returns that port.
    /// </summary>
    public async Task<int> ProvisionAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var port = await _reservation.GetOrReserveAsync(agentId, cancellationToken);
        try
        {
            return await _containerManager.EnsureContainerAsync(agentId, port, cancellationToken);
        }
        catch (PortReservationConflictException)
        {
            var newPort = await _reservation.ReassignAsync(agentId, cancellationToken);
            return await _containerManager.EnsureContainerAsync(agentId, newPort, cancellationToken);
        }
    }
}
