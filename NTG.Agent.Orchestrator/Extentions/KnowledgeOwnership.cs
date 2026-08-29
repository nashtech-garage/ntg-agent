using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Extentions;

/// <summary>Identity of an agent, for user-facing messages and knowledge-base pickers.</summary>
public sealed record AgentSummary(Guid Id, string Name);

/// <summary>
/// Knowledge-base ownership, resolved straight off <see cref="AgentDbContext"/>. Deliberately
/// extension methods rather than an injected service: the controllers that need this are
/// constructed positionally across many test call sites, and adding a constructor parameter to them
/// would churn every one.
/// <para>Two invariants live here, and nowhere else:</para>
/// <list type="bullet">
/// <item><description>An agent's knowledge base is <c>KnowledgeOwnerAgentId ?? Id</c>. An owner
/// always has <c>KnowledgeOwnerAgentId == null</c>, so a join target must itself be an owner —
/// guests never own, and ownership never chains.</description></item>
/// <item><description>Only <see cref="AgentKind.Outer"/> agents have a knowledge base at all.
/// For an inner agent every lookup here resolves to <see langword="null"/>: no container, no
/// workspace, no documents.</description></item>
/// </list>
/// </summary>
public static class KnowledgeOwnershipExtensions
{
    /// <summary>
    /// The knowledge base <paramref name="agentId"/> reads and writes: its owner's id, or its own id
    /// when it owns its KB. Returns <see langword="null"/> when the agent has no knowledge base —
    /// an inner agent, or a row that no longer exists.
    /// </summary>
    public static async Task<Guid?> GetKnowledgeOwnerIdAsync(
        this AgentDbContext db, Guid agentId, CancellationToken cancellationToken = default)
        => await db.Agents
            .Where(a => a.Id == agentId && a.AgentKind == AgentKind.Outer)
            .Select(a => (Guid?)(a.KnowledgeOwnerAgentId ?? a.Id))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Distinct knowledge-base owners — one LightRAG container per entry. Inner agents are excluded,
    /// so the startup reconciler no longer boots a container for them.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> GetKnowledgeOwnerIdsAsync(
        this AgentDbContext db, CancellationToken cancellationToken = default)
        => await db.Agents
            .Where(a => a.AgentKind == AgentKind.Outer)
            .Select(a => a.KnowledgeOwnerAgentId ?? a.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>Agents that are guests in this owner's knowledge base. Excludes the owner itself.</summary>
    public static async Task<IReadOnlyList<AgentSummary>> GetKnowledgeGuestsAsync(
        this AgentDbContext db, Guid ownerAgentId, CancellationToken cancellationToken = default)
        => await db.Agents
            .Where(a => a.KnowledgeOwnerAgentId == ownerAgentId)
            .Select(a => new AgentSummary(a.Id, a.Name))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Validates an agent's request to join <paramref name="targetAgentId"/>'s knowledge base.
    /// Returns <see langword="null"/> when the join is allowed, otherwise a user-facing reason.
    /// <paramref name="movingAgentId"/> is the agent being moved, or <see langword="null"/> when
    /// the agent does not exist yet (creation).
    /// </summary>
    public static async Task<string?> ValidateJoinTargetAsync(
        this AgentDbContext db, Guid targetAgentId, Guid? movingAgentId, CancellationToken cancellationToken = default)
    {
        if (movingAgentId is Guid moving && moving == targetAgentId)
            return "An agent cannot join its own knowledge base.";

        var target = await db.Agents
            .Where(a => a.Id == targetAgentId)
            .Select(a => new { a.Name, a.KnowledgeOwnerAgentId, a.AgentKind })
            .FirstOrDefaultAsync(cancellationToken);

        if (target is null)
            return $"Knowledge base owner '{targetAgentId}' was not found.";

        if (target.AgentKind != AgentKind.Outer)
            return $"'{target.Name}' is an inner agent and has no knowledge base.";

        // Ownership never chains: joining a guest would make the real owner ambiguous.
        if (target.KnowledgeOwnerAgentId is not null)
            return $"'{target.Name}' is itself a guest in another knowledge base. " +
                   "Join the owning agent's knowledge base instead.";

        return null;
    }
}
