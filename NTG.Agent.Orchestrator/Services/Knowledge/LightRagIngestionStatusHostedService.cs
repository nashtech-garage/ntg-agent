using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Advances documents through the LightRAG ingestion pipeline without blocking the upload request.
/// Periodically polls LightRAG (via <see cref="IKnowledgeService.CheckIngestStatusAsync"/>) for every
/// document still in <see cref="DocumentStatus.Processing"/> and flips it to
/// <see cref="DocumentStatus.Completed"/> (filling in <c>KnowledgeDocId</c>) or
/// <see cref="DocumentStatus.Failed"/> (filling in <c>ErrorMessage</c>). This keeps the DB correct
/// even when no user is watching the page.
/// </summary>
public sealed class LightRagIngestionStatusHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagIngestionStatusHostedService> _logger;

    public LightRagIngestionStatusHostedService(
        IServiceProvider serviceProvider,
        IOptions<LightRagSettings> settings,
        ILogger<LightRagIngestionStatusHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LightRAG ingestion status poll failed.");
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();

        var pending = await db.Documents
            .Where(d => d.Status == DocumentStatus.Processing && d.TrackId != null)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var changed = false;
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
                        // Still Processing — leave it for the next tick.
                        break;
                }
            }
            catch (Exception ex)
            {
                // A single agent's container being down shouldn't stop the rest; retry next tick.
                _logger.LogWarning(ex, "Failed to check ingestion status for documentId={DocumentId} (agentId={AgentId}).", doc.Id, doc.AgentId);
            }
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }
}
