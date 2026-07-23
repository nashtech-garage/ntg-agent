namespace NTG.Agent.Orchestrator.Exceptions;

/// <summary>
/// Raised when probing a model provider fails. The message is already
/// user-friendly (e.g. "Authentication failed — check the API key.") and never
/// contains the API key or a raw provider exception.
/// </summary>
public class ProviderProbeException : Exception
{
    public ProviderProbeException(string message) : base(message)
    {
    }
}
