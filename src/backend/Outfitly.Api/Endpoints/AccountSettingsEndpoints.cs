using Outfitly.Api.Authentication;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Outfitly.Api.Endpoints;

public static class AccountSettingsEndpoints
{
    public static void MapAccountSettingsEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPatch("/me/profile", async (ProfileRequest request, HttpContext context, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken ct) =>
        {
            var user = await accounts.ChangeProfileAsync(CurrentActor.Id(context.User), request.DisplayName, request.Username, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Results.Ok(user);
        }).RequireAuthorization(CurrentActor.PolicyName);

        auth.MapPost("/me/change-password", async (PasswordRequest request, HttpContext context, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken ct) =>
        {
            await accounts.ChangePasswordAsync(CurrentActor.Id(context.User), request.CurrentPassword, request.NewPassword, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName).RequireRateLimiting("account-access");

        auth.MapPost("/me/email-change", async (EmailChangeRequest request, HttpContext context, AccountService accounts,
            IUnitOfWork unitOfWork, IConfirmationEmailSender sender, CancellationToken ct) =>
        {
            var email = await accounts.RequestEmailChangeAsync(CurrentActor.Id(context.User), request.Email, request.CurrentPassword, ct);
            await unitOfWork.SaveChangesAsync(ct);
            var sent = true;
            try { await sender.SendAsync(email, ct); }
            catch (EmailDeliveryException) { sent = false; }
            return Results.Json(new { pendingEmail = email.Email, confirmationEmailSent = sent }, statusCode: 202);
        }).RequireAuthorization(CurrentActor.PolicyName).RequireRateLimiting("account-access");

        auth.MapDelete("/me/email-change", async (HttpContext context, AccountService accounts, IUnitOfWork unitOfWork, CancellationToken ct) =>
        {
            await accounts.CancelEmailChangeAsync(CurrentActor.Id(context.User), ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName);

        auth.MapPost("/confirm-email-change", async (TokenRequest request, HttpContext context, AccountService accounts,
            IUnitOfWork unitOfWork, CancellationToken ct) =>
        {
            var userId = await accounts.ConfirmEmailChangeAsync(request.Token, ct);
            await unitOfWork.SaveChangesAsync(ct);
            if (context.User.Identity?.IsAuthenticated == true && CurrentActor.Id(context.User) == userId)
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireRateLimiting("account-access");
    }

    public sealed record ProfileRequest(string DisplayName, string Username);
    public sealed record PasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record EmailChangeRequest(string Email, string CurrentPassword);
    public sealed record TokenRequest(string Token);
}
