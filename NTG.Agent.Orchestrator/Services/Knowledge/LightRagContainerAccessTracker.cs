namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Thread-safe singleton that tracks the last time each agent's LightRAG container
/// was accessed. Used by <see cref="LightRagContainerIdleShutdownService"/> to decide
/// when a container can be stopped to reclaim RAM.
/// </summary>
public sealed class LightRagContainerAccessTracker
{
    private readonly Dictionary<Guid, DateTime> _lastAccess = new();
    private readonly object _lock = new();

    /// <summary>Record that the given agent's container was just accessed.</summary>
    public void Touch(Guid agentId)
    {
        lock (_lock)
        {
            _lastAccess[agentId] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns the last access time for the given agent, or <c>null</c> if the agent
    /// has never been tracked. Callers should treat a missing entry as "unknown"
    /// (i.e. don't shut down a container we've never seen accessed).
    /// </summary>
    public DateTime? GetLastAccess(Guid agentId)
    {
        lock (_lock)
        {
            return _lastAccess.TryGetValue(agentId, out var ts) ? ts : null;
        }
    }

    /// <summary>Remove the tracking entry for an agent (e.g. after its container is removed).</summary>
    public void Remove(Guid agentId)
    {
        lock (_lock)
        {
            _lastAccess.Remove(agentId);
        }
    }
}