namespace Microsoft.KernelMemory.Service;

public sealed class HttpErrorsEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == 413)
        {
            return Results.Problem(
                statusCode: e.StatusCode,
                detail: e.Message);
        }
    }
}
