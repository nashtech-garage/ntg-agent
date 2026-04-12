namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// Controls the reasoning strategy used by the agent when generating a response.
/// </summary>
public enum AgentMode
{
    /// <summary>Fast mode – returns the final answer immediately with no visible reasoning.</summary>
    Fast = 0,

    /// <summary>Thinking mode – surfaces the model's chain-of-thought before the final answer.</summary>
    Thinking = 1
}
