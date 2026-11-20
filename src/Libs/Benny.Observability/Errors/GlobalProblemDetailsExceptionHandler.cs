using System.Diagnostics;
using Benny.Common.Domain.Context;
using Benny.Common.Domain.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Benny.Observability.Errors;

public sealed class GlobalProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, errorCode) = exception switch
        {
            BennyBusinessException businessEx => (businessEx.HttpStatusCode, businessEx.ErrorCode, businessEx.ErrorCode),
            OperationCanceledException => (StatusCodes.Status504GatewayTimeout, "Request Timeout / Deadline Exceeded", "DEADLINE_EXCEEDED"),
            TimeoutException => (StatusCodes.Status504GatewayTimeout, "Downstream Timeout", "DOWNSTREAM_TIMEOUT"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "INTERNAL_ERROR")
        };

        if (statusCode >= 500)
        {
            logger.LogError(exception, "Unhandled exception ({ErrorCode}) for CorrelationId={CorrelationId}",
                errorCode, RequestContextHolder.Current?.CorrelationId ?? "unknown");
        }
        else
        {
            logger.LogWarning("Business/Client error ({ErrorCode}): {Message}", errorCode, exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Type = $"https://benny.io/errors/{errorCode.ToLowerInvariant()}",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problemDetails.Extensions["correlationId"] = RequestContextHolder.Current?.CorrelationId;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
