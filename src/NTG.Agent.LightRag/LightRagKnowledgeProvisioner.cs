using NTG.Agent.Common.Knowledge;

namespace NTG.Agent.LightRag;

/// <summary>
/// LightRAG's take on the provider-neutral <see cref="IKnowledgeProvisioner"/>: provisioning
/// an agent means starting its dedicated container (reachable through the gateway by name);
/// deprovisioning stops and removes that container.
/// </summary>
public sealed class LightRagKnowledgeProvisioner : IKnowledgeProvisioner
{
    private readonly ILightRagContainerManager _containerManager;

    public LightRagKnowledgeProvisioner(ILightRagContainerManager containerManager)
    {
        _containerManager = containerManager;
    }

    public Task ProvisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
        => _containerManager.EnsureContainerAsync(agentId, cancellationToken);

    public Task DeprovisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
        => _containerManager.StopAndRemoveContainerAsync(agentId, cancellationToken);
}
