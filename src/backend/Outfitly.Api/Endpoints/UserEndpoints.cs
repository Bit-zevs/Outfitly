using Outfitly.Api.Authentication;
using Outfitly.Api.Contracts;
using Outfitly.Application;

namespace Outfitly.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var users = app.MapGroup("/api/users");
        users.MapGet("/", (string? search, int? page, int? pageSize, PublicProfiles profiles, CancellationToken ct) =>
            profiles.SearchAsync(search, page ?? 1, pageSize ?? 20, ct));
        users.MapGet("/{username}", (string username, HttpContext http, SocialProfiles profiles, CancellationToken ct) => profiles.ProfileAsync(username, http.User.Identity?.IsAuthenticated == true ? CurrentActor.Id(http.User) : null, ct));
        users.MapGet("/{username}/items", async (string username, int? page, int? pageSize, PublicProfiles profiles, CancellationToken ct) =>
        {
            var result = await profiles.ItemsAsync(username, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(new { Items = result.Items.Select(item => WardrobeResponses.Item(item, $"/api/wardrobe/items/{item.Id}/photo")),
                result.Total, result.Page, result.PageSize });
        });
        users.MapGet("/{username}/outfits", async (string username, int? page, int? pageSize, PublicProfiles profiles, CancellationToken ct) =>
        {
            var result = await profiles.OutfitsAsync(username, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(new { Items = result.Items.Select(View), result.Total, result.Page, result.PageSize });
        });
        users.MapGet("/{username}/items/{id:guid}", async (string username, Guid id, PublicProfiles profiles, CancellationToken ct) =>
            WardrobeResponses.Item(await profiles.ItemAsync(username, id, ct), $"/api/wardrobe/items/{id}/photo"));
        users.MapGet("/{username}/outfits/{id:guid}", async (string username, Guid id, PublicProfiles profiles, CancellationToken ct) =>
            View(await profiles.OutfitAsync(username, id, ct)));
    }

    private static object View(OutfitView outfit) => WardrobeResponses.Outfit(outfit,
        id => $"/api/wardrobe/outfits/{outfit.Id}/items/{id}/photo");
}
