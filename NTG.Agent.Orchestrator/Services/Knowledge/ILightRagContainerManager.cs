namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Manages the lifecycle of per-agent LightRAG app containers
/// (<c>lightrag-agent-{agentId}</c>) on the host Docker daemon. Each container is
/// isolated by LightRAG's <c>WORKSPACE</c> env var and points at the single shared
/// <c>lightrag-postgres</c>.
/// </summary>
public interface ILightRagContainerManager
{
    /// <summary>Pulls the configured LightRAG image if it is not already present locally.</summary>
    Task EnsureImagePulledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently ensures the agent's container exists and is running, returning the
    /// host port it is published on. If a container already exists and is running, its
    /// current port is returned. Otherwise a container is created on <paramref name="desiredPort"/>
    /// (when supplied and free) or on a freshly-found free port.
    /// </summary>
    Task<int> EnsureContainerAsync(Guid agentId, int? desiredPort, CancellationToken cancellationToken = default);

    /// <summary>Stops and removes the agent's container. No-op if it does not exist.</summary>
    Task StopAndRemoveContainerAsync(Guid agentId, CancellationToken cancellationToken = default);
}
