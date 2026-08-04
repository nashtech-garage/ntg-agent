namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// Request used to probe a provider before an agent is saved. Carries the
/// provider selection plus the credentials the admin just entered. The API key
/// is held only for the duration of the request and is never echoed back.
/// </summary>
public record ProviderProbeRequest(string ProviderName, string? Endpoint, string ApiKey);

/// <summary>Result of a connection test — never includes the model list or the key.</summary>
public record ProviderTestResult(bool Success, string Message);

/// <summary>The live list of model (or Azure deployment) names fetched from a provider.</summary>
public record ProviderModelsResponse(List<string> Models);
