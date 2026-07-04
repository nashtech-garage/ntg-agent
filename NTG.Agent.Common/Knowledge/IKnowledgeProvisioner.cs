namespace NTG.Agent.Common.Knowledge;

/// <summary>
/// Provider-neutral hook for per-agent knowledge infrastructure lifecycle. Providers that
/// allocate resources per agent (e.g. LightRAG's one-container-per-agent model) implement
/// this to create/tear down those resources when an agent is created/deleted; providers
/// with shared infrastructure (e.g. Kernel Memory) register a no-op.
/// </summary>
public interface IKnowledgeProvisioner
{
    /// <summary>Ensures the agent's knowledge backend is provisioned and ready.</summary>
    Task ProvisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>Tears down the agent's knowledge backend resources (called on agent deletion).</summary>
    Task DeprovisionAgentAsync(Guid agentId, CancellationToken cancellationToken = default);
}
