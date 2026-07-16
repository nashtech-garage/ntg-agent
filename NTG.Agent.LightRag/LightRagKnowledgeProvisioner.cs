using NTG.Agent.Common.Knowledge;

namespace NTG.Agent.LightRag;

/// <summary>
/// LightRAG's take on the provider-neutral <see cref="IKnowledgeProvisioner"/>: provisioning
/// an agent means reserving its identity-bound port and starting its dedicated container;
/// deprovisioning stops and removes that container.
/// </summary>
public sealed class LightRagKnowledgeProvisioner : IKnowledgeProvisioner
{
    private readonly ILightRagProvisioner _provisioner;
    private readonly ILightRagContainerManager _containerManager;

    public LightRagKnowledgeProvisioner(ILightRagProvisioner provisioner, ILightRagContainerManager containerManager)
    {
        _provisioner = provisioner;
        _containerManager = containerManager;
    }

    public Task ProvisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
        => _provisioner.ProvisionAsync(agentId, cancellationToken);

    public Task DeprovisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
        => _containerManager.StopAndRemoveContainerAsync(agentId, cancellationToken);
}
