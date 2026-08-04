using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// Admin-only endpoints that probe a model provider before an agent is saved:
/// validate credentials and list available models. These run without an agent ID
/// (the create flow has no agent yet) and mirror the trust model of the existing
/// MCP <c>connect</c> endpoint — the API key is forwarded to the provider only for
/// the duration of the request and is never logged or echoed back.
/// </summary>
[Route("api/agentadmin/provider")]
[ApiController]
[Authorize(Roles = "Admin")]
public class ProviderProbeController : ControllerBase
{
    private readonly IProviderModelService _providerModelService;

    public ProviderProbeController(IProviderModelService providerModelService)
    {
        _providerModelService = providerModelService ?? throw new ArgumentNullException(nameof(providerModelService));
    }

    /// <summary>Tests the provider credentials using the cheapest non-billable call (a model list).</summary>
    /// <response code="200">Returns the test result (success flag + friendly message).</response>
    /// <response code="400">If the provider name is unknown.</response>
    [HttpPost("test-connection")]
    public async Task<ActionResult<ProviderTestResult>> TestConnection(
        [FromBody] ProviderProbeRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || !_providerModelService.IsKnownProvider(request.ProviderName))
        {
            return BadRequest("Unknown or missing provider.");
        }

        var result = await _providerModelService.TestConnectionAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Fetches the live list of model (or Azure deployment) names for the provider.</summary>
    /// <response code="200">Returns the model list.</response>
    /// <response code="400">If the provider name is unknown or the probe fails.</response>
    [HttpPost("models")]
    public async Task<ActionResult<ProviderModelsResponse>> GetModels(
        [FromBody] ProviderProbeRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || !_providerModelService.IsKnownProvider(request.ProviderName))
        {
            return BadRequest("Unknown or missing provider.");
        }

        try
        {
            var models = await _providerModelService.FetchModelsAsync(request, cancellationToken);
            return Ok(new ProviderModelsResponse(models));
        }
        catch (ProviderProbeException ex)
        {
            // Message is already user-friendly and key-free.
            return BadRequest(ex.Message);
        }
    }
}
