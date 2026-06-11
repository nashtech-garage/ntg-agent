namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Provisions an agent's LightRAG container on its identity-bound reserved port
/// (reserve → ensure → reassign-and-retry on external port conflict).
/// </summary>
public interface ILightRagProvisioner
{
    /// <summary>
    /// Ensures the agent's container is running on its reserved port and returns that port.
    /// </summary>
    Task<int> ProvisionAsync(Guid agentId, CancellationToken cancellationToken = default);
}
