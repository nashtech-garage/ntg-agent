namespace NTG.Agent.LightRag;

/// <summary>
/// Thrown when the Docker daemon that hosts the per-agent LightRAG containers cannot be
/// reached (e.g. the <c>ssh -L 2375:/var/run/docker.sock</c> tunnel is down). Surfacing this
/// typed exception instead of leaking the raw <c>Docker.DotNet</c>/<see cref="System.Net.Sockets.SocketException"/>
/// gives chat/upload callers a clean, actionable "knowledge backend unavailable" signal.
/// </summary>
public class LightRagDaemonUnavailableException : Exception
{
    public string DockerHost { get; }

    public LightRagDaemonUnavailableException(string dockerHost)
        : base($"LightRAG Docker daemon at '{(string.IsNullOrWhiteSpace(dockerHost) ? "local socket" : dockerHost)}' " +
               "is unreachable. Is the SSH tunnel (ssh -L 2375:/var/run/docker.sock) up?")
    {
        DockerHost = dockerHost;
    }
}
