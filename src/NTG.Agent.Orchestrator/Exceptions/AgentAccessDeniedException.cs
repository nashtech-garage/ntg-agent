namespace NTG.Agent.Orchestrator.Exceptions;

public class AgentAccessDeniedException : Exception
{
    public Guid AgentId { get; }

    public AgentAccessDeniedException(Guid agentId)
        : base("You do not have access to this agent.")
    {
        AgentId = agentId;
    }
}
