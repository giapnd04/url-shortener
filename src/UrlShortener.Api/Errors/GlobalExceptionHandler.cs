using Microsoft.AspNetCore.Diagnostics;

namespace UrlShortener.Api.Errors;

/// <summary>
/// Last-resort handler, registered after the specific ones (CodeGenerationExhaustedHandler).
/// - Unexpected exception: log Error with the full exception (the log scope carries the correlation
///   id), answer 500 ProblemDetails with traceId only. Never put exception text in the response: it
///   can contain SQL, server names or stack details.
/// - Client went away (request aborted): not a server bug, so log Information and do not write a body.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request aborted by the client");
            context.Response.StatusCode = 499; // de-facto "client closed request"; nobody reads the body
            return true;
        }

        logger.LogError(exception, "Unhandled exception");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "Quote the traceId when reporting this problem.",
            },
            // Exception deliberately not passed: in Development the default writer would add its details.
        });
    }
}
