using System.Collections.Concurrent;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Request-scoped buffer of narration lines describing skill activity (a skill loading, a surface
/// rendering) during a chat run. Because the buffer is scoped, both the outer agent and any inner
/// agents it delegates to (created within the same request) write to the same instance — so activity
/// deep inside an inner agent still surfaces to the UI. <see cref="Agents.AgentService"/> drains it
/// while streaming and forwards each line to the browser as a <c>SkillNotice</c> chunk.
/// </summary>
public sealed class SkillActivityLog
{
    private readonly ConcurrentQueue<string> _pending = new();

    public void Add(string line) => _pending.Enqueue(line);

    public IEnumerable<string> DrainPending()
    {
        while (_pending.TryDequeue(out var line))
        {
            yield return line;
        }
    }

    /// <summary>
    /// Empties the buffer without handing anything back, for a run whose client has nowhere to show
    /// the narration. Separate from <see cref="DrainPending"/> for the same reason its counterpart
    /// on <see cref="Agents.RenderableToolCapture"/> is: that one is a lazy iterator, so emptying
    /// through it only happens if the caller remembers to enumerate what it returns.
    /// </summary>
    public void DiscardPending()
    {
        while (_pending.TryDequeue(out _))
        {
        }
    }
}
