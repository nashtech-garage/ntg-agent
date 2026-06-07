namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Wake signal that lets <see cref="LightRagIngestionStatusHostedService"/> stay parked (no polling,
/// no DB queries) while nothing is being ingested. Upload endpoints call <see cref="Notify"/> after
/// persisting a Processing document; the worker drains all Processing docs, then waits again.
///
/// Backed by a <see cref="SemaphoreSlim"/> with max count 1 so bursts of notifications coalesce into
/// a single pending wake — and a notification that arrives just as the worker is about to park is
/// not lost (the worker's next <see cref="WaitAsync"/> returns immediately).
/// </summary>
public sealed class IngestionStatusSignal
{
    private readonly SemaphoreSlim _signal = new(initialCount: 0, maxCount: 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake is already pending — nothing more to do.
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);
}
