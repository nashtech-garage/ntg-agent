using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

/// <summary>
/// The shared middleware pipeline applied to every agent chat client.
/// </summary>
public static class ChatClientPipeline
{
    private const string TelemetrySourceName = "NTG.Agent.Orchestrator";

    /// <summary>
    /// Wraps <paramref name="inner"/> with function invocation and OpenTelemetry.
    /// <paramref name="configureOptions"/> is used by reasoning paths to set the
    /// <see cref="ChatOptions.RawRepresentationFactory"/> carrying provider reasoning parameters.
    /// </summary>
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
