using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Outfitly.Domain;
using Outfitly.Application;
using Outfitly.Application.Authentication;

namespace Outfitly.Api.Errors;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            EmailNotConfirmedException => StatusCodes.Status403Forbidden,
            InvalidConfirmationTokenException => StatusCodes.Status400BadRequest,
            InvalidPasswordResetTokenException => StatusCodes.Status400BadRequest,
            ConfirmationTooFrequentException => StatusCodes.Status429TooManyRequests,
            EmailDeliveryException => StatusCodes.Status503ServiceUnavailable,
            PhotoTooLargeException => StatusCodes.Status413PayloadTooLarge,
            UnsupportedPhotoException => StatusCodes.Status415UnsupportedMediaType,
            BadHttpRequestException bad => bad.StatusCode,
            WardrobeConcurrencyException => StatusCodes.Status409Conflict,
            AccountConflictException => StatusCodes.Status409Conflict,
            UsernameConflictException => StatusCodes.Status409Conflict,
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
            413 => "Photo is too large",
            415 => "Unsupported photo format",
            429 => "Too many account requests",
            503 => "Email delivery unavailable",
            _ => "An unexpected error occurred"
        };
        context.Response.StatusCode = status;
        var code = exception switch
        {
            UsernameConflictException => "username_taken",
            EmailNotConfirmedException => "email_not_confirmed",
            InvalidConfirmationTokenException => "invalid_confirmation_token",
            InvalidPasswordResetTokenException => "invalid_password_reset_token",
            ConfirmationTooFrequentException => "confirmation_too_frequent",
            EmailDeliveryException => "email_delivery_failed",
            _ => null
        };
        if (exception is ConfirmationTooFrequentException) context.Response.Headers.RetryAfter = "60";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception is ArgumentException || exception is WardrobeConcurrencyException ? exception.Message : null,
            Extensions = code is null ? [] : new Dictionary<string, object?> { ["code"] = code }
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
