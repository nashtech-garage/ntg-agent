namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>Represents a single directed handoff connection between two agents.</summary>
public record AgentHandoffDto(Guid Id, Guid SourceAgentId, Guid TargetAgentId, string? Description);

/// <summary>Represents a participant agent in a handoff workflow with its connections.</summary>
public record AgentParticipantDto(Guid Id, string Name, string? Description, bool IsSelectable);

/// <summary>The full handoff workflow for a given agent — its outgoing and incoming connections.</summary>
public class AgentHandoffWorkflowDto
{
    public Guid AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public List<AgentHandoffDto> OutgoingHandoffs { get; set; } = [];
    public List<AgentHandoffDto> IncomingHandoffs { get; set; } = [];
    public List<AgentParticipantDto> AllAgents { get; set; } = [];
}

/// <summary>Request to add or update handoff targets for a source agent.</summary>
public class UpdateAgentHandoffsRequest
{
    public Guid SourceAgentId { get; set; }
    public List<HandoffTargetRequest> Targets { get; set; } = [];
}

/// <summary>A single target in a handoff request.</summary>
public class HandoffTargetRequest
{
    public Guid TargetAgentId { get; set; }
    public string? Description { get; set; }
}

/// <summary>Full workflow graph — all published agents and all handoff edges.</summary>
public class WorkflowGraphDto
{
    public List<AgentParticipantDto> Agents { get; set; } = [];
    public List<AgentHandoffDto> Edges { get; set; } = [];
}

/// <summary>Request to create a single directed handoff edge.</summary>
public class CreateHandoffEdgeRequest
{
    public Guid SourceAgentId { get; set; }
    public Guid TargetAgentId { get; set; }
    public string? Description { get; set; }
}
