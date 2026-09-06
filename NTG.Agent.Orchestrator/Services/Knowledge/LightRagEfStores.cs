using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.LightRag;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// EF-backed implementation of the LightRAG provider's persistence seams. This is the only
/// place where the LightRAG provider touches the Orchestrator's database: knowledge-base ownership
/// for the startup reconciler and the workspace resolver, and ingestion progress on <c>Document</c>.
/// The ownership rules themselves live in <see cref="KnowledgeOwnershipExtensions"/>.
/// </summary>
public sealed class LightRagEfAgentStore : ILightRagAgentStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    // Deliberately NOT the request's AgentDbContext. Ownership is resolved from inside
    // IKnowledgeService, and the chat path starts a knowledge search *concurrently* with building
    // the agent (AgentService's speculative prefetch). Sharing the request-scoped context would put
    // two EF queries on one DbContext at once — "A second operation was started on this context
    // instance". Each lookup gets its own scope and context instead; it is one short read, and
    // LightRagWorkspaceResolver caches the result for the rest of the request.
    public LightRagEfAgentStore(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<IReadOnlyList<Guid>> GetKnowledgeOwnerIdsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.GetKnowledgeOwnerIdsAsync(cancellationToken);
    }

    public async Task<Guid?> GetKnowledgeOwnerAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.GetKnowledgeOwnerIdAsync(agentId, cancellationToken);
    }
}

/// <summary>EF-backed store the LightRAG ingestion-status worker polls and updates.</summary>
public sealed class LightRagEfIngestionStore : ILightRagIngestionStore
{
    private readonly AgentDbContext _db;

    public LightRagEfIngestionStore(AgentDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProcessingDocument>> GetProcessingDocumentsAsync(CancellationToken cancellationToken = default)
        => await _db.Documents
            .Where(d => d.Status == DocumentStatus.Processing && d.TrackId != null)
            .Select(d => new ProcessingDocument(d.Id, d.AgentId, d.TrackId!))
            .ToListAsync(cancellationToken);

    public async Task ApplyUpdatesAsync(IReadOnlyList<IngestionStatusUpdate> updates, CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0) return;

        var ids = updates.Select(u => u.DocumentId).ToList();
        var documents = await _db.Documents
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        foreach (var update in updates)
        {
            if (!documents.TryGetValue(update.DocumentId, out var doc))
                continue;

            doc.Status = update.Status;
            doc.KnowledgeDocId = update.KnowledgeDocId;
            doc.ErrorMessage = update.ErrorMessage;
            doc.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
