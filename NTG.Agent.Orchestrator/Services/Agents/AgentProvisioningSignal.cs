namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Wake signal that lets the agent-provisioning worker stay parked (no polling, no DB queries)
/// while nothing is waiting to be provisioned. Agent creation / reprovision calls <see cref="Notify"/>
/// after persisting an agent in <c>Provisioning</c> state; the worker drains all pending agents,
/// then waits again.
///
/// Backed by a <see cref="SemaphoreSlim"/> with max count 1 so bursts of notifications coalesce into
/// a single pending wake — and a notification that arrives just as the worker is about to park is
/// not lost (the worker's next <see cref="WaitAsync"/> returns immediately). Mirrors
/// <c>IngestionStatusSignal</c>.
/// </summary>
public sealed class AgentProvisioningSignal
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
