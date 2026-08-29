namespace NTG.Agent.LightRag;

/// <summary>
/// Manages the lifecycle of per-knowledge-base LightRAG app containers
/// (<c>lightrag-agent-{ownerAgentId}</c>) on the host Docker daemon. Each container is
/// isolated by LightRAG's <c>WORKSPACE</c> env var and points at the single shared
/// <c>lightrag-postgres</c>.
/// <para>
/// Every id below is the id of the agent that <i>owns</i> a knowledge base, never a guest's.
/// Agents sharing a knowledge base share one container between them, so passing a guest's id here
/// would create a second container and silently split them apart. Resolve with
/// <see cref="LightRagWorkspaceResolver"/> before calling.
/// </para>
/// </summary>
public interface ILightRagContainerManager
{
    /// <summary>
    /// Returns true if the Docker daemon answers a ping; false if it is unreachable
    /// (e.g. the SSH tunnel is down). Never throws for connectivity failures.
    /// </summary>
    Task<bool> IsDaemonReachableAsync(CancellationToken cancellationToken = default);

    /// <summary>Pulls the configured LightRAG image if it is not already present locally.</summary>
    Task EnsureImagePulledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently ensures the knowledge base's container exists, is running, and is serving
    /// through the gateway. A healthy running container is reused; otherwise it is
    /// (re)created. Containers publish no host ports — the gateway reaches them by
    /// name over the shared Docker network.
    /// </summary>
    Task EnsureContainerAsync(Guid ownerAgentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops and removes the knowledge base's container. No-op if it does not exist.
    /// Destroys the container for every agent sharing that knowledge base, so callers must
    /// establish that no guests remain before calling.
    /// </summary>
    Task StopAndRemoveContainerAsync(Guid ownerAgentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the knowledge base's container without removing it, so it can be restarted later
    /// via <see cref="EnsureContainerAsync"/>. No-op if the container is not running.
    /// </summary>
    Task StopContainerAsync(Guid ownerAgentId, CancellationToken cancellationToken = default);
}
