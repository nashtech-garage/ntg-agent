using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// A captured server-side tool call whose result the browser should render as generative UI.
/// <see cref="Arguments"/> is the argument map (serialized as a JSON object) and <see cref="Result"/>
/// is the tool's raw result text.
/// </summary>
public sealed record CapturedToolCall(string CallId, string Name, IReadOnlyDictionary<string, object?> Arguments, string Result);

/// <summary>
/// Request-scoped buffer of renderable tool calls captured during a chat run. Because the buffer is
/// scoped, both the outer agent and any inner agents it delegates to (created within the same request)
/// write to the same instance — so a tool invoked deep inside an inner agent still surfaces to the UI.
/// <see cref="AgentService"/> drains it while streaming and forwards each entry to the browser.
/// </summary>
public sealed class RenderableToolCapture
{
    // Server-side tools whose call + result are surfaced to the browser to render (e.g. weather card).
    // Kept private and frozen so the set cannot be mutated at runtime; IsRenderable is the API surface.
    private static readonly FrozenSet<string> RenderableToolNames =
        new[] { "get_weather" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<CapturedToolCall> _pending = new();

    public static bool IsRenderable(string toolName) => RenderableToolNames.Contains(toolName);

    public void Add(CapturedToolCall call) => _pending.Enqueue(call);

    public IEnumerable<CapturedToolCall> DrainPending()
    {
        while (_pending.TryDequeue(out var call))
        {
            yield return call;
        }
    }
}
