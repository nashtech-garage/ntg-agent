namespace NTG.Agent.LightRag;

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
    /// Idempotently ensures the agent's container exists and is running, bound to the
    /// agent's reserved <paramref name="hostPort"/>, and returns that port. A healthy
    /// container already running on the reserved port is reused; otherwise it is
    /// (re)created on the reserved port.
    /// </summary>
    /// <exception cref="PortReservationConflictException">
    /// The reserved port is held on the host by an external process; the caller should
    /// reassign a new reserved port and retry.
    /// </exception>
    Task<int> EnsureContainerAsync(Guid agentId, int hostPort, CancellationToken cancellationToken = default);

    /// <summary>Stops and removes the agent's container. No-op if it does not exist.</summary>
    Task StopAndRemoveContainerAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the agent's container without removing it, so it can be restarted later
    /// via <see cref="EnsureContainerAsync"/>. No-op if the container is not running.
    /// </summary>
    Task StopContainerAsync(Guid agentId, CancellationToken cancellationToken = default);
}
