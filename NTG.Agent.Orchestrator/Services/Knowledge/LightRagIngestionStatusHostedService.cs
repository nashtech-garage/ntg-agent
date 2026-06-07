using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Advances documents through the LightRAG ingestion pipeline without blocking the upload request.
/// Polls LightRAG (via <see cref="IKnowledgeService.CheckIngestStatusAsync"/>) for every document
/// still in <see cref="DocumentStatus.Processing"/> and flips it to
/// <see cref="DocumentStatus.Completed"/> (filling in <c>KnowledgeDocId</c>) or
/// <see cref="DocumentStatus.Failed"/> (filling in <c>ErrorMessage</c>).
///
/// The worker is <b>event-driven</b>: it only polls while documents are processing. When none
/// remain it parks on <see cref="IngestionStatusSignal.WaitAsync"/> (no timer, no DB queries) until
/// an upload calls <see cref="IngestionStatusSignal.Notify"/>. A drain pass also runs once at
/// startup to recover any documents left Processing by a previous run.
/// </summary>
public sealed class LightRagIngestionStatusHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IngestionStatusSignal _signal;
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagIngestionStatusHostedService> _logger;

    public LightRagIngestionStatusHostedService(
        IServiceProvider serviceProvider,
        IngestionStatusSignal signal,
        IOptions<LightRagSettings> settings,
        ILogger<LightRagIngestionStatusHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _signal = signal;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain: keep polling while any document is still Processing. The first iteration
                // also clears leftovers from a previous run.
                while (await PollOnceAsync(stoppingToken))
                    await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LightRAG ingestion status poll failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }

            // Idle: nothing to do — park until an upload signals new work (no polling while parked).
            try
            {
                await _signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Polls each Processing document once; returns true if any document is still Processing.</summary>
    private async Task<bool> PollOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();

        var pending = await db.Documents
            .Where(d => d.Status == DocumentStatus.Processing && d.TrackId != null)
            .ToListAsync(ct);

        if (pending.Count == 0) return false;

        var changed = false;
        var stillProcessing = 0;
        foreach (var doc in pending)
        {
            try
            {
                var result = await knowledge.CheckIngestStatusAsync(doc.AgentId, doc.TrackId!, ct);
                switch (result.Status)
                {
                    case DocumentStatus.Completed:
                        doc.Status = DocumentStatus.Completed;
                        doc.KnowledgeDocId = result.KnowledgeDocId;
                        doc.UpdatedAt = DateTime.UtcNow;
                        changed = true;
                        _logger.LogInformation("Ingestion completed: documentId={DocumentId} knowledgeDocId={KnowledgeDocId}", doc.Id, result.KnowledgeDocId);
                        break;
                    case DocumentStatus.Failed:
                        doc.Status = DocumentStatus.Failed;
                        doc.KnowledgeDocId = result.KnowledgeDocId;
                        doc.ErrorMessage = result.ErrorMessage;
                        doc.UpdatedAt = DateTime.UtcNow;
                        changed = true;
                        _logger.LogWarning("Ingestion failed: documentId={DocumentId} reason={Reason}", doc.Id, result.ErrorMessage);
                        break;
                    default:
                        // Still Processing — keep it for the next pass.
                        stillProcessing++;
                        break;
                }
            }
            catch (Exception ex)
            {
                // A single agent's container being down shouldn't stop the rest; retry next pass.
                stillProcessing++;
                _logger.LogWarning(ex, "Failed to check ingestion status for documentId={DocumentId} (agentId={AgentId}).", doc.Id, doc.AgentId);
            }
        }

        if (changed)
            await db.SaveChangesAsync(ct);

        return stillProcessing > 0;
    }
}
