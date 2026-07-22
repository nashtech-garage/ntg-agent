using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Agents.Clients;

public static class ChatClientPipeline
{
    private const string TelemetrySourceName = "NTG.Agent.Orchestrator";

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
