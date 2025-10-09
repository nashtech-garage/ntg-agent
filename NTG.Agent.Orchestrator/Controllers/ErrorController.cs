using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NTG.Agent.Orchestrator.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class ErrorController  : ControllerBase
{
    private readonly ILogger<ErrorController> _logger;
    public ErrorController(ILogger<ErrorController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Route("/error-development")]
    public IActionResult HandleErrorDevelopment([FromServices] IHostEnvironment hostEnvironment)
    {
        if (!hostEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        var exceptionHandlerFeature = HttpContext.Features.Get<IExceptionHandlerFeature>()!;
        _logger.LogError(exceptionHandlerFeature.Error, "Unhandled exception occurred while processing request.");

        return Problem(
            detail: exceptionHandlerFeature.Error.StackTrace,
            title: exceptionHandlerFeature.Error.Message);
    }

    [Route("/error")]
    public IActionResult HandleError([FromServices] IHostEnvironment hostEnvironment)
    {
        var exceptionHandlerFeature = HttpContext.Features.Get<IExceptionHandlerFeature>()!;
        _logger.LogError(exceptionHandlerFeature.Error, "Unhandled exception occurred while processing request.");

        return Problem();
    }
}
