using NTG.Agent.Common.Knowledge;

namespace NTG.Agent.LightRag;

/// <summary>
/// LightRAG's take on the provider-neutral <see cref="IKnowledgeProvisioner"/>: provisioning an
/// agent means starting the container behind its knowledge base (reachable through the gateway by
/// name); deprovisioning stops and removes that container.
/// <para>
/// Both operations are per <i>knowledge base</i>, not per agent, so each guards against acting on
/// someone else's container — see the individual methods.
/// </para>
/// </summary>
public sealed class LightRagKnowledgeProvisioner : IKnowledgeProvisioner
{
    private readonly ILightRagContainerManager _containerManager;
    private readonly LightRagWorkspaceResolver _resolver;

    public LightRagKnowledgeProvisioner(
        ILightRagContainerManager containerManager,
        LightRagWorkspaceResolver resolver)
    {
        _containerManager = containerManager;
        _resolver = resolver;
    }

    /// <summary>
    /// Ensures the container behind this agent's knowledge base is running. For a guest that is the
    /// owner's container, which is usually already up — so joining an existing knowledge base
    /// completes in seconds rather than waiting on a cold container boot.
    /// </summary>
    public async Task ProvisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var ownerAgentId = await _resolver.GetOwnerAgentIdAsync(agentId, cancellationToken);
        // No knowledge base to provision — an inner agent. Nothing to do, and nothing to fail on.
        if (ownerAgentId is null) return;

        await _containerManager.EnsureContainerAsync(ownerAgentId.Value, cancellationToken);
    }

    /// <summary>
    /// Tears down the container behind this agent's knowledge base — but only when this agent owns
    /// it. A guest shares someone else's container, and an inner agent never had one, so both are
    /// no-ops: removing the container in either case would break every agent still using it.
    /// </summary>
    /// <remarks>
    /// Callers must invoke this <i>before</i> deleting the agent row, since ownership is resolved by
    /// reading it.
    /// </remarks>
    public async Task DeprovisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var ownerAgentId = await _resolver.GetOwnerAgentIdAsync(agentId, cancellationToken);
        if (ownerAgentId is null) return;           // inner agent — nothing was ever provisioned
        if (ownerAgentId.Value != agentId) return;  // guest — the container belongs to the owner

        await _containerManager.StopAndRemoveContainerAsync(agentId, cancellationToken);
    }
}
