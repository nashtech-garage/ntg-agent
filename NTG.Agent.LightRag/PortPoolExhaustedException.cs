namespace NTG.Agent.LightRag;

/// <summary>
/// Thrown when no free host port remains in the configured reservation range
/// [<c>PortRangeStart</c>, <c>PortRangeEnd</c>] to assign to an agent.
/// </summary>
public class PortPoolExhaustedException : Exception
{
    public int RangeStart { get; }

    public int RangeEnd { get; }

    public PortPoolExhaustedException(int rangeStart, int rangeEnd)
        : base($"LightRAG port pool exhausted: no free host port in range {rangeStart}-{rangeEnd}.")
    {
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
    }
}
