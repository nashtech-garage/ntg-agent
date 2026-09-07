namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>Request body for the live thinking-support probe.</summary>
public class ThinkingSupportRequest
{
    public string ModelId { get; set; } = string.Empty;
}

/// <summary>
/// Verdict of the live thinking-support probe. <see cref="SupportsThinking"/> is true only when
/// the provider accepted a small test request carrying the thinking parameter;
/// <see cref="Error"/> carries the provider's failure reason otherwise.
/// </summary>
public class ThinkingSupportResult
{
    public bool SupportsThinking { get; set; }
    public string? Error { get; set; }
}
