namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// Moves an agent to a different knowledge base.
/// </summary>
/// <param name="KnowledgeOwnerAgentId">
/// The agent owning the knowledge base to join, or <c>null</c> to leave for a brand-new
/// knowledge base of this agent's own (which provisions a fresh container). Documents already
/// in the old knowledge base stay behind either way.
/// </param>
public record MoveKnowledgeBaseRequest(Guid? KnowledgeOwnerAgentId);
