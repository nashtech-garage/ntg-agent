using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// The shared middleware pipeline applied to every agent chat client.
/// </summary>
/// <remarks>
/// The <c>.AsBuilder().UseFunctionInvocation().UseOpenTelemetry(…)</c> chain was previously
/// copy-pasted into every provider/mode branch (~5 near-identical blocks). Centralizing it here
/// means each provider factory supplies only what genuinely differs — the reasoning-specific
/// <see cref="ChatOptions"/> configuration — and the common concerns stay in one place.
/// </remarks>
public static class ChatClientPipeline
{
    /// <summary>Telemetry source name shared by all orchestrator chat clients.</summary>
    private const string TelemetrySourceName = "NTG.Agent.Orchestrator";

    /// <summary>
    /// Wraps <paramref name="inner"/> with function invocation and OpenTelemetry, optionally
    /// applying reasoning/provider-specific options.
    /// </summary>
    /// <param name="inner">The raw provider chat client (e.g. from <c>GetChatClient().AsIChatClient()</c>).</param>
    /// <param name="configureOptions">
    /// Optional per-call configuration — used by reasoning paths to set the
    /// <see cref="ChatOptions.RawRepresentationFactory"/> that carries the provider's reasoning
    /// parameters. Omitted for plain (non-reasoning) clients.
    /// </param>
    /// <returns>The built <see cref="IChatClient"/> with the standard pipeline applied.</returns>
    public static IChatClient BuildStandard(this IChatClient inner, Action<ChatOptions>? configureOptions = null)
    {
        var builder = inner.AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: TelemetrySourceName, configure: cfg => cfg.EnableSensitiveData = true);

        if (configureOptions is not null)
        {
            builder.ConfigureOptions(configureOptions);
        }

        return builder.Build();
    }
}
