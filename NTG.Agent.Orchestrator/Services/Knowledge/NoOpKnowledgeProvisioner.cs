using NTG.Agent.Common.Knowledge;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Provisioner for knowledge providers with shared infrastructure (e.g. Kernel Memory),
/// where creating or deleting an agent requires no per-agent resources.
/// </summary>
public sealed class NoOpKnowledgeProvisioner : IKnowledgeProvisioner
{
    public Task ProvisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeprovisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
