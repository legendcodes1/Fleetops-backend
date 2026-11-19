using FleetOps.Domain.Exceptions;
using FleetOps.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Errors;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            PreconditionRequiredException => StatusCodes.Status428PreconditionRequired,
            ConcurrencyConflictException => StatusCodes.Status412PreconditionFailed,
            PersistenceConflictException => StatusCodes.Status409Conflict,
            DomainRuleException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        if (status == 500) logger.LogError(exception, "Unhandled request failure");
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = status switch { 400 => "Invalid request", 409 => "Resource conflict", 412 => "Precondition failed", 428 => "Precondition required", _ => "An unexpected error occurred" },
                Detail = status == 500 ? null : exception.Message
            }
        });
        return true;
    }
}
