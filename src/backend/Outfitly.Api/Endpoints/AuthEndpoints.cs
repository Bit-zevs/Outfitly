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
        auth.MapPatch("/me/username", async (UsernameRequest request, HttpContext context, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            var user = await accounts.ChangeUsernameAsync(CurrentActor.Id(context.User), request.Username, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Results.Ok(user);
        }).RequireAuthorization(CurrentActor.PolicyName);
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        auth.MapPost("/register", async (RegisterRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, IConfirmationEmailSender sender, CancellationToken cancellationToken) =>
        {
            var user = await accounts.RegisterAsync(request.Email, request.Password, request.DisplayName, request.Username, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var sent = true;
            try { await sender.SendAsync(user.Confirmation, cancellationToken); }
            catch (EmailDeliveryException) { sent = false; }
            return Results.Created("/api/auth/me", new { user.Id, user.Email, user.DisplayName, user.Username, confirmationEmailSent = sent });
        }).RequireRateLimiting("account-access");
        auth.MapPost("/confirm-email", async (ConfirmEmailRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            await accounts.ConfirmEmailAsync(request.Token, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("account-access");
        auth.MapPost("/resend-confirmation", async (ResendConfirmationRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, IConfirmationEmailSender sender, CancellationToken cancellationToken) =>
        {
            var email = await accounts.ResendConfirmationAsync(request.Email, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (email is not null) await sender.SendAsync(email, cancellationToken);
            return Results.Json(new { message = "If confirmation is needed, an email has been sent." }, statusCode: 202);
        }).RequireRateLimiting("account-access");
        auth.MapPost("/forgot-password", async (ResendConfirmationRequest request, AccountService accounts,
            IUnitOfWork unitOfWork, IConfirmationEmailSender sender, CancellationToken cancellationToken) =>
        {
            var email = await accounts.RequestPasswordResetAsync(request.Email, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (email is not null)
            {
                try { await sender.SendAsync(email, cancellationToken); }
                catch (EmailDeliveryException) { /* Keep the response independent of account existence. */ }
            }
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
            IUnitOfWork unitOfWork, HttpContext context, CancellationToken cancellationToken) =>
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
            return Results.Ok(result.User);
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
        auth.MapGet("/me", async (HttpContext context, AccountService accounts, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = await accounts.GetUserAsync(CurrentActor.Id(context.User), cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireAuthorization(CurrentActor.PolicyName);
    }

    public sealed record RegisterRequest(string Email, string Password, string DisplayName, string Username);
    public sealed record UsernameRequest(string Username);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record ResetPasswordRequest(string Token, string Password);
    public sealed record ConfirmEmailRequest(string Token);
    public sealed record ResendConfirmationRequest(string Email);
}
