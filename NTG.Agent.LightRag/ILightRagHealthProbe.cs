namespace NTG.Agent.LightRag;

/// <summary>
/// A single-shot readiness check against a knowledge base's LightRAG container, dialed through the
/// nginx gateway (<c>{GatewayUrl}/agents/{ownerAgentId}/health</c>). The gateway answers 502/504
/// for a stopped or still-booting container, so only a 2xx from the app counts as ready.
/// Shared by <see cref="LightRagClientFactory"/> (to decide whether to provision) and
/// <see cref="LightRagContainerManager"/> (to poll a freshly-started container until its
/// ASGI app is actually accepting requests).
/// </summary>
public interface ILightRagHealthProbe
{
    /// <summary>
    /// Returns <see langword="true"/> if the knowledge base's container answered the health request
    /// with a success status, otherwise <see langword="false"/>. Never throws for a normal
    /// unreachable/timeout outcome — those are reported as <see langword="false"/>.
    /// </summary>
    /// <param name="ownerAgentId">
    /// Id of the agent that <i>owns</i> the knowledge base — never a guest's id. Resolve it with
    /// <see cref="LightRagWorkspaceResolver"/> first; a guest id here probes a container that does
    /// not exist.
    /// </param>
    Task<bool> IsHealthyAsync(Guid ownerAgentId, CancellationToken cancellationToken = default);
}
