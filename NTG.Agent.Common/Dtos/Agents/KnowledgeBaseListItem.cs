namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// One joinable knowledge base, identified by the agent that owns it. Backs the
/// "join an existing knowledge base" picker in Admin.
/// </summary>
/// <param name="OwnerAgentId">The founding agent; also the container and workspace key.</param>
/// <param name="OwnerAgentName">The owning agent's name, shown in the picker.</param>
/// <param name="AgentCount">The owner plus every guest sharing it.</param>
/// <param name="DocumentCount">Documents held by the knowledge base, whichever agent uploaded them.</param>
public record KnowledgeBaseListItem(Guid OwnerAgentId, string OwnerAgentName, int AgentCount, int DocumentCount);
