namespace NTG.Agent.Common.Dtos;

/// <summary>
/// Exposes server-side feature flags to the client so UI elements can be
/// conditionally shown or hidden based on server configuration.
/// </summary>
public record FeatureFlagsDto(bool DocumentIntelligenceEnabled);
