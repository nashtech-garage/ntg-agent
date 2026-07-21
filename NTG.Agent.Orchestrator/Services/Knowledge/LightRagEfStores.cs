using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.LightRag;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// EF-backed implementation of the LightRAG provider's persistence seams. This is the only
/// place where the LightRAG provider touches the Orchestrator's database: the agent's resolved
/// port is cached on <c>Agent.LightRagPort</c> and ingestion progress on <c>Document</c>.
/// <para>
/// Note this is a <b>cache</b>, not the authority: ports are allocated from the shared Postgres
/// ledger (<see cref="ILightRagPortReservationStore"/>) so developers sharing one Docker host
/// cannot be handed the same port. Caching it locally keeps the chat hot path free of a
/// cross-database round-trip.
/// </para>
/// </summary>
public sealed class LightRagEfAgentPortStore : ILightRagAgentPortStore
{
    private readonly AgentDbContext _db;

    public LightRagEfAgentPortStore(AgentDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> GetAgentIdsAsync(CancellationToken cancellationToken = default)
        => await _db.Agents.Select(a => a.Id).ToListAsync(cancellationToken);

    public async Task<int?> GetPortAsync(Guid agentId, CancellationToken cancellationToken = default)
        => await _db.Agents
            .Where(a => a.Id == agentId)
            .Select(a => a.LightRagPort)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<(Guid AgentId, int Port)>> GetAssignedPortsAsync(CancellationToken cancellationToken = default)
        => (await _db.Agents
            .Where(a => a.LightRagPort != null && a.LightRagPort > 0)
            .Select(a => new { a.Id, Port = a.LightRagPort!.Value })
            .ToListAsync(cancellationToken))
            .Select(x => (x.Id, x.Port))
            .ToList();

    public async Task SetPortAsync(Guid agentId, int port, CancellationToken cancellationToken = default)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
            ?? throw new InvalidOperationException($"Agent {agentId} not found while reserving a LightRAG port.");

        agent.LightRagPort = port;
        await _db.SaveChangesAsync(cancellationToken);
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
