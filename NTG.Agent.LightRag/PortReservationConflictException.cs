namespace NTG.Agent.LightRag;

/// <summary>
/// Thrown when an agent's reserved host port cannot be bound because an external
/// process already holds it on the Docker host. The caller should obtain a new
/// reserved port (<c>PortReservationService.ReassignAsync</c>) and retry.
/// </summary>
public class PortReservationConflictException : Exception
{
    public Guid AgentId { get; }

    public int Port { get; }

    public PortReservationConflictException(Guid agentId, int port, Exception? innerException = null)
        : base($"Reserved port {port} for agent {agentId} is already in use on the Docker host.", innerException)
    {
        AgentId = agentId;
        Port = port;
    }
}
