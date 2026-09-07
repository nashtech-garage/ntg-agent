namespace NTG.Agent.LightRag.CustomExceptions;

/// <summary>
/// Thrown when the Docker daemon that hosts the per-agent LightRAG containers cannot be
/// reached — the server is down, the firewall rule for :2376 is missing, or the client
/// certificate was rejected. Surfacing this typed exception instead of leaking the raw
/// <c>Docker.DotNet</c>/<see cref="System.Net.Sockets.SocketException"/> gives chat/upload
/// callers a clean, actionable "knowledge backend unavailable" signal.
/// </summary>
public class LightRagDaemonUnavailableException : Exception
{
    public string DockerHost { get; }

    public LightRagDaemonUnavailableException(string dockerHost)
        : base($"LightRAG Docker daemon at '{(string.IsNullOrWhiteSpace(dockerHost) ? "local socket" : dockerHost)}' " +
               "is unreachable. Check that the server is up, that the firewall allows inbound 2376 " +
               "from this machine, and that LightRag:DockerCertPath points at a valid client certificate.")
    {
        DockerHost = dockerHost;
    }
}
