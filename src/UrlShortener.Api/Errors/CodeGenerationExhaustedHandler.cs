using Microsoft.AspNetCore.Diagnostics;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Api.Errors;

/// <summary>Bounded retry ran out: a controlled 503 instead of a generic 500 or an endless loop (US-01).</summary>
public sealed class CodeGenerationExhaustedHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not CodeGenerationExhaustedException)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Could not allocate a short code.",
                Detail = "Please retry the request.",
            },
        });
    }
}
