using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Outfitly.Application.Authentication;

namespace Outfitly.Api.Authentication;

public sealed class SessionCookieEvents(AccountService accounts) : CookieAuthenticationEvents
{
    public const string SessionIdClaim = "outfitly:session";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(context.Principal?.FindFirstValue(SessionIdClaim), out var sessionId) ||
            await accounts.ValidateSessionAsync(userId, sessionId, context.HttpContext.RequestAborted) is null)
            context.RejectPrincipal();
    }

    public override Task RedirectToLogin(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
