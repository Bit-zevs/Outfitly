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
        app.MapGet("/api/wardrobe", async (HttpContext context, ItemCatalog catalog) => WardrobeResponses.Page(
            await catalog.GetPublicItemsAsync(CatalogRequest.Parse(context.Request.Query), context.RequestAborted), false));
        app.MapGet("/api/wardrobe/outfits", (WardrobeService service) => service.GetPublicOutfits()
            .Select(outfit => WardrobeResponses.Outfit(outfit, id => $"/api/wardrobe/outfits/{outfit.Id}/items/{id}/photo")));
        app.MapGet("/api/shared/items/{token}", (string token, WardrobeService service) =>
            service.GetSharedItem(token) is { } item ? Results.Ok(WardrobeResponses.Item(item, $"/api/shared/items/{token}/photo")) : Results.NotFound());
        app.MapGet("/api/shared/outfits/{token}", (string token, WardrobeService service) =>
            service.GetSharedOutfit(token) is { } outfit ? Results.Ok(WardrobeResponses.Outfit(outfit, id => $"/api/shared/outfits/{token}/items/{id}/photo")) : Results.NotFound());

        var items = app.MapGroup("/api/items").RequireAuthorization(CurrentActor.PolicyName);
        items.MapGet("/", async (HttpContext context, ItemCatalog catalog) => WardrobeResponses.Page(
            await catalog.GetMyItemsAsync(CurrentActor.Id(context.User), CatalogRequest.Parse(context.Request.Query), context.RequestAborted), true));
        items.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            WardrobeResponses.Item(service.GetItem(CurrentActor.Id(user), id), $"/api/items/{id}/photo"));
        items.MapPost("/", (ItemRequest request, HttpRequest httpRequest, ClaimsPrincipal user, WardrobeService service) =>
        {
            var actor = CurrentActor.Id(user);
            var item = service.CreateItem(actor, request.Name, request.Category, request.Color,
                request.Size, request.Brand, request.Description, WardrobeResponses.ExternalPhotoInput(request.PhotoUrl, Guid.Empty, httpRequest));
            return Results.Created($"/api/items/{item.Id}", WardrobeResponses.Item(service.GetItem(actor, item.Id), $"/api/items/{item.Id}/photo"));
        }).AddEndpointFilter<CommitChangesFilter>();
        items.MapPut("/{id:guid}", (Guid id, ItemRequest request, HttpRequest httpRequest, ClaimsPrincipal user, WardrobeService service) =>
        {
            var actor = CurrentActor.Id(user);
            var photoUrl = WardrobeResponses.ExternalPhotoInput(request.PhotoUrl, id, httpRequest, service.GetItem(actor, id));
            service.UpdateItem(actor, id, request.Name, request.Category, request.Color,
                request.Size, request.Brand, request.Description, photoUrl);
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
        items.MapGet("/{id:guid}/share-links", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            service.GetShareLinks(CurrentActor.Id(user), ShareTargetType.WardrobeItem, id)
                .Select(link => new ShareLinkResponse(link.Id, link.Token, $"/api/shared/items/{link.Token}")));

        var outfits = app.MapGroup("/api/outfits").RequireAuthorization(CurrentActor.PolicyName);
        outfits.MapGet("/", (ClaimsPrincipal user, WardrobeService service) => service.GetMyOutfits(CurrentActor.Id(user))
            .Select(outfit => WardrobeResponses.Outfit(outfit, id => $"/api/items/{id}/photo")));
        outfits.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            WardrobeResponses.Outfit(service.GetOutfit(CurrentActor.Id(user), id), itemId => $"/api/items/{itemId}/photo"));
        outfits.MapPost("/", (OutfitRequest request, ClaimsPrincipal user, WardrobeService service) =>
        {
            var actor = CurrentActor.Id(user);
            var outfit = service.CreateOutfit(actor, request.Name, request.Description);
            return Results.Created($"/api/outfits/{outfit.Id}", WardrobeResponses.Outfit(service.GetOutfit(actor, outfit.Id), id => $"/api/items/{id}/photo"));
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
        outfits.MapGet("/{id:guid}/share-links", (Guid id, ClaimsPrincipal user, WardrobeService service) =>
            service.GetShareLinks(CurrentActor.Id(user), ShareTargetType.Outfit, id)
                .Select(link => new ShareLinkResponse(link.Id, link.Token, $"/api/shared/outfits/{link.Token}")));
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
