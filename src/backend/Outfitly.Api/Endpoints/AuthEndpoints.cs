using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Outfitly.Api.Authentication;
using Outfitly.Application;
using Outfitly.Application.Authentication;

namespace Outfitly.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapAccountSettingsEndpoints();
        auth.MapPatch("/me/username", async (UsernameRequest request, HttpContext context, AccountService accounts, ISupportAdminAccess supportAdmins,
            IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            var user = await accounts.ChangeUsernameAsync(CurrentActor.Id(context.User), request.Username, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Results.Ok(user with { IsSupportAdmin = supportAdmins.IsAdmin(user.Id) });
        }).RequireAuthorization(CurrentActor.PolicyName);
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        auth.MapPost("/register", async (RegisterRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, IAnonymousAccountMail mail, CancellationToken cancellationToken) =>
        {
            ConfirmationEmail confirmation;
            try
            {
                var user = await accounts.RegisterAsync(request.Email, request.DisplayName, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                confirmation = user.Confirmation;
            }
            catch (AccountConflictException) { return ConfirmationAccepted(); }
            await mail.EnqueueAsync(confirmation, cancellationToken);
            return ConfirmationAccepted();
        }).RequireRateLimiting("account-access");
        auth.MapPost("/confirm-email", async (ConfirmEmailRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            await accounts.ConfirmEmailAsync(request.Token, request.Password, request.Username, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("account-access");
        auth.MapPost("/resend-confirmation", async (ResendConfirmationRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, IAnonymousAccountMail mail, CancellationToken cancellationToken) =>
        {
            ConfirmationEmail? email;
            try
            {
                email = await accounts.ResendConfirmationAsync(request.Email, cancellationToken);
                if (email is not null) await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ConfirmationTooFrequentException) { return ConfirmationAccepted(); }
            catch (WardrobeConcurrencyException) { return ConfirmationAccepted(); }
            if (email is not null) await mail.EnqueueAsync(email, cancellationToken);
            return ConfirmationAccepted();
        }).RequireRateLimiting("account-access");
        auth.MapPost("/forgot-password", async (ResendConfirmationRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, PasswordResetMail mail, CancellationToken cancellationToken) =>
        {
            try
            {
                var pending = await accounts.RequestPasswordResetAsync(request.Email, cancellationToken);
                if (pending is not null)
                {
                    mail.Stage(pending);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }
            catch (WardrobeConcurrencyException) { /* Another request already updated the account. */ }
            return Results.Json(new { message = "If the account is eligible, a password reset email has been sent." }, statusCode: 202);
        }).RequireRateLimiting("account-access");
        auth.MapPost("/reset-password", async (ResetPasswordRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            await accounts.ResetPasswordAsync(request.Token, request.Password, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("account-access");
        auth.MapPost("/login", async (LoginRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, HttpContext context, ISupportAdminAccess supportAdmins, CancellationToken cancellationToken) =>
        {
            var result = await accounts.LoginAsync(request.Email, request.Password, cancellationToken);
            if (result is null) return Results.Problem(statusCode: 401, title: "Invalid email or password");
            // Switching accounts in this browser must not leave its previous session active.
            if (context.User.Identity?.IsAuthenticated == true &&
                Guid.TryParse(context.User.FindFirstValue(SessionCookieEvents.SessionIdClaim), out var previousSession))
                await accounts.RevokeSessionAsync(CurrentActor.Id(context.User), previousSession, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, result.User.Id.ToString()),
                new Claim(SessionCookieEvents.SessionIdClaim, result.Session.Id.ToString())
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties
                {
                    IsPersistent = false, AllowRefresh = false,
                    IssuedUtc = new DateTimeOffset(result.Session.ExpiresAtUtc).AddHours(-8),
                    ExpiresUtc = new DateTimeOffset(result.Session.ExpiresAtUtc)
                });
            return Results.Ok(result.User with { IsSupportAdmin = supportAdmins.IsAdmin(result.User.Id) });
        }).RequireRateLimiting("account-access");
        auth.MapPost("/logout", async (HttpContext context, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            var sessionId = Guid.Parse(context.User.FindFirstValue(SessionCookieEvents.SessionIdClaim)!);
            await accounts.RevokeSessionAsync(CurrentActor.Id(context.User), sessionId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName);
        auth.MapGet("/me", async (HttpContext context, AccountService accounts, ISupportAdminAccess supportAdmins, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = await accounts.GetUserAsync(CurrentActor.Id(context.User), cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(user with { IsSupportAdmin = supportAdmins.IsAdmin(user.Id) });
        }).RequireAuthorization(CurrentActor.PolicyName);
    }

    private static IResult ConfirmationAccepted() =>
        Results.Accepted(value: new { message = "If confirmation is needed, check your email for instructions." });

    public sealed record RegisterRequest(string Email, string DisplayName);
    public sealed record UsernameRequest(string Username);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record ResetPasswordRequest(string Token, string Password);
    public sealed record ConfirmEmailRequest(string Token, string Password, string Username);
    public sealed record ResendConfirmationRequest(string Email);
}
