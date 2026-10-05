using System.Security.Claims;
using Outfitly.Api.Authentication;
using Outfitly.Api.Contracts;
using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Api.Endpoints;

public static class WardrobeEndpoints
{
    public static void MapWardrobeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wardrobe", (WardrobeService service) => service.GetPublicItems());
        app.MapGet("/api/wardrobe/outfits", (WardrobeService service) => service.GetPublicOutfits());
        app.MapGet("/api/shared/items/{token}", (string token, WardrobeService service) =>
            service.GetSharedItem(token) is { } item ? Results.Ok(item) : Results.NotFound());
        app.MapGet("/api/shared/outfits/{token}", (string token, WardrobeService service) =>
            service.GetSharedOutfit(token) is { } outfit ? Results.Ok(outfit) : Results.NotFound());

        var items = app.MapGroup("/api/items").RequireAuthorization(CurrentActor.PolicyName);
        items.MapGet("/", (ClaimsPrincipal user, WardrobeService service) => service.GetMyItems(CurrentActor.Id(user)));
        items.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            service.GetItem(CurrentActor.Id(user), id));
        items.MapPost("/", (ItemRequest request, ClaimsPrincipal user, WardrobeService service) =>
        {
            var actor = CurrentActor.Id(user);
            var item = service.CreateItem(actor, request.Name, request.Category, request.Color,
                request.Size, request.Brand, request.Description, request.PhotoUrl);
            return Results.Created($"/api/items/{item.Id}", service.GetItem(actor, item.Id));
        }).AddEndpointFilter<CommitChangesFilter>();
        items.MapPut("/{id:guid}", (Guid id, ItemRequest request, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.UpdateItem(CurrentActor.Id(user), id, request.Name, request.Category, request.Color,
                request.Size, request.Brand, request.Description, request.PhotoUrl);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        items.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.DeleteItem(CurrentActor.Id(user), id);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        items.MapPatch("/{id:guid}/publication", (Guid id, PublicationRequest request, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.SetPublication(CurrentActor.Id(user), ShareTargetType.WardrobeItem, id, request.IsPublic);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        items.MapPost("/{id:guid}/share-links", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            CreateLink(CurrentActor.Id(user), ShareTargetType.WardrobeItem, id, service))
            .AddEndpointFilter<CommitChangesFilter>();

        var outfits = app.MapGroup("/api/outfits").RequireAuthorization(CurrentActor.PolicyName);
        outfits.MapGet("/", (ClaimsPrincipal user, WardrobeService service) => service.GetMyOutfits(CurrentActor.Id(user)));
        outfits.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            service.GetOutfit(CurrentActor.Id(user), id));
        outfits.MapPost("/", (OutfitRequest request, ClaimsPrincipal user, WardrobeService service) =>
        {
            var actor = CurrentActor.Id(user);
            var outfit = service.CreateOutfit(actor, request.Name, request.Description);
            return Results.Created($"/api/outfits/{outfit.Id}", service.GetOutfit(actor, outfit.Id));
        }).AddEndpointFilter<CommitChangesFilter>();
        outfits.MapPut("/{id:guid}", (Guid id, OutfitRequest request, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.UpdateOutfit(CurrentActor.Id(user), id, request.Name, request.Description);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        outfits.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.DeleteOutfit(CurrentActor.Id(user), id);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        outfits.MapPost("/{id:guid}/items/{itemId:guid}", (Guid id, Guid itemId, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.AddItemToOutfit(CurrentActor.Id(user), id, itemId);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        outfits.MapDelete("/{id:guid}/items/{itemId:guid}", (Guid id, Guid itemId, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.RemoveItemFromOutfit(CurrentActor.Id(user), id, itemId);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        outfits.MapPatch("/{id:guid}/publication", (Guid id, PublicationRequest request, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.SetPublication(CurrentActor.Id(user), ShareTargetType.Outfit, id, request.IsPublic);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        outfits.MapPost("/{id:guid}/share-links", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            CreateLink(CurrentActor.Id(user), ShareTargetType.Outfit, id, service))
            .AddEndpointFilter<CommitChangesFilter>();
        app.MapDelete("/api/share-links/{id:guid}", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
        {
            service.DeactivateShareLink(CurrentActor.Id(user), id);
            return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
    }

    private static IResult CreateLink(Guid actor, ShareTargetType type, Guid id, WardrobeService service)
    {
        var link = service.CreateShareLink(actor, type, id);
        var resource = type == ShareTargetType.WardrobeItem ? "items" : "outfits";
        var url = $"/api/shared/{resource}/{link.Token}";
        return Results.Created(url, new ShareLinkResponse(link.Id, link.Token, url));
    }
}
