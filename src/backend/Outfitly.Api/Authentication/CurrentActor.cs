using System.Security.Claims;

namespace Outfitly.Api.Authentication;

internal static class CurrentActor
{
    internal const string PolicyName = "WardrobeOwner";

    internal static Guid Id(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("A valid authenticated user identifier is required.");

    internal static bool HasValidId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty;
}
