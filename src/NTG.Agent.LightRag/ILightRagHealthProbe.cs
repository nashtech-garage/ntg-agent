namespace NTG.Agent.LightRag;

/// <summary>
/// A single-shot readiness check against an agent's LightRAG container, dialed through the
/// nginx gateway (<c>{GatewayUrl}/agents/{agentId}/health</c>). The gateway answers 502/504
/// for a stopped or still-booting container, so only a 2xx from the app counts as ready.
/// Shared by <see cref="LightRagClientFactory"/> (to decide whether to provision) and
/// <see cref="LightRagContainerManager"/> (to poll a freshly-started container until its
/// ASGI app is actually accepting requests).
/// </summary>
public interface ILightRagHealthProbe
{
    /// <summary>
    /// Returns <see langword="true"/> if the agent's container answered the health request
    /// with a success status, otherwise <see langword="false"/>. Never throws for a normal
    /// unreachable/timeout outcome — those are reported as <see langword="false"/>.
    /// </summary>
    Task<bool> IsHealthyAsync(Guid agentId, CancellationToken cancellationToken = default);
}
