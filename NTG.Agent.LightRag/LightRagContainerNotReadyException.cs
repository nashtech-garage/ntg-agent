namespace NTG.Agent.LightRag;

/// <summary>
/// Thrown when a freshly-started LightRAG container's HTTP app did not begin serving
/// (answer <c>GET /health</c>) on its published port within the configured readiness
/// window (<see cref="LightRagSettings.ReadinessTimeoutSeconds"/>). Surfacing this instead
/// of returning a not-yet-serving endpoint prevents the "response ended prematurely" race
/// where the first request hits the container before its ASGI app is up.
/// </summary>
public class LightRagContainerNotReadyException : Exception
{
    public string ContainerName { get; }

    public int Port { get; }

    public LightRagContainerNotReadyException(string containerName, int port, TimeSpan timeout)
        : base($"LightRAG container {containerName} did not become ready on port {port} within {timeout.TotalSeconds:0}s.")
    {
        ContainerName = containerName;
        Port = port;
    }
}
