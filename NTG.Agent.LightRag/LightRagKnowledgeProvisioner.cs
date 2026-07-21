using NTG.Agent.Common.Knowledge;

namespace NTG.Agent.LightRag;

/// <summary>
/// LightRAG's take on the provider-neutral <see cref="IKnowledgeProvisioner"/>: provisioning
/// an agent means reserving its identity-bound port and starting its dedicated container;
/// deprovisioning stops and removes that container and releases the port back to the pool.
/// </summary>
public sealed class LightRagKnowledgeProvisioner : IKnowledgeProvisioner
{
    private readonly ILightRagProvisioner _provisioner;
    private readonly ILightRagContainerManager _containerManager;
    private readonly PortReservationService _reservation;

    public LightRagKnowledgeProvisioner(
        ILightRagProvisioner provisioner,
        ILightRagContainerManager containerManager,
        PortReservationService reservation)
    {
        _provisioner = provisioner;
        _containerManager = containerManager;
        _reservation = reservation;
    }

    public Task ProvisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
        => _provisioner.ProvisionAsync(agentId, cancellationToken);

    /// <summary>
    /// Tears down the agent's container and then releases its port reservation. Order matters: the
    /// container must be gone before the port is returned to the pool, otherwise a new agent could
    /// reserve a port that is still bound on the host.
    /// </summary>
    public async Task DeprovisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await _containerManager.StopAndRemoveContainerAsync(agentId, cancellationToken);
        await _reservation.ReleaseAsync(agentId, cancellationToken);
    }
}
