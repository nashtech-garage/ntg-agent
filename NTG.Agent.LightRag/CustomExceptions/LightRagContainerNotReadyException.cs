namespace NTG.Agent.LightRag.CustomExceptions;

/// <summary>
/// Thrown when a freshly-started LightRAG container's HTTP app did not begin serving
/// (answer <c>GET health</c> through the gateway) within the configured readiness
/// window (<see cref="LightRagSettings.ReadinessTimeoutSeconds"/>). Surfacing this instead
/// of returning a not-yet-serving endpoint prevents the "response ended prematurely" race
/// where the first request hits the container before its ASGI app is up. Can also mean the
/// gateway itself is down — the probe cannot tell the two apart.
/// </summary>
public class LightRagContainerNotReadyException : Exception
{
    public string ContainerName { get; }

    public LightRagContainerNotReadyException(string containerName, TimeSpan timeout)
        : base($"LightRAG container {containerName} did not become ready through the gateway within " +
               $"{timeout.TotalSeconds:0}s. If other agents are also failing, check that the " +
               "lightrag-gateway container is running.")
    {
        ContainerName = containerName;
    }
}
