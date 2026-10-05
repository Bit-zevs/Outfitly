using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Outfitly.Domain;

namespace Outfitly.Api.Errors;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            InvalidOperationException when exception.TargetSite?.DeclaringType?.Assembly == typeof(Outfit).Assembly
                => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        var title = status switch
        {
            400 => "Invalid request",
            403 => "Access denied",
            404 => "Resource not found",
            409 => "The operation conflicts with the current state",
            _ => "An unexpected error occurred"
        };
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == 400 ? exception.Message : null
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
