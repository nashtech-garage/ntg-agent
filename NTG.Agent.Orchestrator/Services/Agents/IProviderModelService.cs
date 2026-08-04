using NTG.Agent.Common.Dtos.Agents;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Probes a model provider on behalf of the admin UI: validates credentials and
/// lists the available models/deployments. All provider-specific logic lives here.
/// </summary>
public interface IProviderModelService
{
    /// <summary>Returns whether the given provider name is one we know how to probe.</summary>
    bool IsKnownProvider(string providerName);

    /// <summary>
    /// Validates credentials using the cheapest non-billable call (a model list).
    /// Never throws for provider/credential errors — failures are returned as
    /// <see cref="ProviderTestResult.Success"/> = false with a friendly message.
    /// </summary>
    Task<ProviderTestResult> TestConnectionAsync(ProviderProbeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists model IDs (or Azure deployment names) for the provider, sorted and
    /// deduplicated. Throws <see cref="Exceptions.ProviderProbeException"/> with a
    /// friendly message on credential/connectivity errors or an unknown provider.
    /// </summary>
    Task<List<string>> FetchModelsAsync(ProviderProbeRequest request, CancellationToken cancellationToken = default);
}
