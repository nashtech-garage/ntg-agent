using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Orchestrator.Exceptions;

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
        
        // Handle rate limit exceptions specially
        if (exceptionHandlerFeature.Error is AnonymousRateLimitExceededException rateLimitEx)
        {
            return HandleRateLimitException(rateLimitEx);
        }
        
        LogUnhandledException(_logger, exceptionHandlerFeature.Error);

        return Problem(
            detail: exceptionHandlerFeature.Error.StackTrace,
            title: exceptionHandlerFeature.Error.Message);
    }

    [Route("/error")]
    public IActionResult HandleError([FromServices] IHostEnvironment hostEnvironment)
    {
        var exceptionHandlerFeature = HttpContext.Features.Get<IExceptionHandlerFeature>()!;
        
        // Handle rate limit exceptions specially
        if (exceptionHandlerFeature.Error is AnonymousRateLimitExceededException rateLimitEx)
        {
            return HandleRateLimitException(rateLimitEx);
        }
        
        LogUnhandledException(_logger, exceptionHandlerFeature.Error);

        return Problem();
    }

    private ObjectResult HandleRateLimitException(AnonymousRateLimitExceededException ex)
    {
        LogRateLimitExceeded(_logger, ex.CurrentCount, ex.MaxMessages, ex.BlockReason);
        
        return StatusCode(429, new
        {
            error = "RateLimitExceeded",
            message = ex.Message,
            details = new
            {
                currentCount = ex.CurrentCount,
                maxMessages = ex.MaxMessages,
                resetAt = ex.ResetAt,
                isReadOnlyMode = ex.IsReadOnlyMode,
                blockReason = ex.BlockReason
            }
        });
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled exception occurred while processing request.")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Rate limit exceeded: {CurrentCount}/{MaxMessages} messages, reason: {BlockReason}")]
    private static partial void LogRateLimitExceeded(ILogger logger, int currentCount, int maxMessages, string blockReason);
}
