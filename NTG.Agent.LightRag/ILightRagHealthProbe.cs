namespace NTG.Agent.LightRag;

/// <summary>
/// A single-shot readiness check against a LightRAG container's published HTTP port.
/// Any HTTP response — even 401/404 — means the app is serving; only a connection-level
/// failure or timeout counts as not-ready. Shared by <see cref="LightRagClientFactory"/>
/// (to decide whether to provision) and <see cref="LightRagContainerManager"/> (to poll a
/// freshly-started container until its ASGI app is actually accepting requests).
/// </summary>
public interface ILightRagHealthProbe
{
    /// <summary>
    /// Returns <see langword="true"/> if the container on <paramref name="port"/> answered
    /// an HTTP request, otherwise <see langword="false"/>. Never throws for a normal
    /// unreachable/timeout outcome — those are reported as <see langword="false"/>.
    /// </summary>
    Task<bool> IsHealthyAsync(int port, CancellationToken cancellationToken = default);
}
