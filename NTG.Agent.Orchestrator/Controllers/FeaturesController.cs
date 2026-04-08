using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Common.Dtos;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// Exposes server-side feature flags to the client application.
/// This allows the UI to conditionally enable or disable features based on server configuration
/// without exposing sensitive configuration details.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class FeaturesController(IDocumentAnalysisService documentAnalysisService) : ControllerBase
{
    /// <summary>
    /// Returns the current feature flags for the application.
    /// </summary>
    /// <returns>A <see cref="FeatureFlagsDto"/> containing the feature flag values.</returns>
    [HttpGet]
    public ActionResult<FeatureFlagsDto> GetFeatures()
    {
        return Ok(new FeatureFlagsDto(DocumentIntelligenceEnabled: documentAnalysisService.IsEnabled));
    }
}
