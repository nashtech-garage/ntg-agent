using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NTG.Agent.Orchestrator.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public partial class ErrorController : ControllerBase
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
        LogUnhandledException(_logger, exceptionHandlerFeature.Error);

        return Problem(
            detail: exceptionHandlerFeature.Error.StackTrace,
            title: exceptionHandlerFeature.Error.Message);
    }

    [Route("/error")]
    public IActionResult HandleError([FromServices] IHostEnvironment hostEnvironment)
    {
        var exceptionHandlerFeature = HttpContext.Features.Get<IExceptionHandlerFeature>()!;
        LogUnhandledException(_logger, exceptionHandlerFeature.Error);

        return Problem();
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled exception occurred while processing request.")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);
}
