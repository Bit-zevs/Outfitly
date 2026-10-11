using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Outfitly.Api.Authentication;
using Outfitly.Api.Contracts;
using Outfitly.Application;

namespace Outfitly.Api.Endpoints;

public static class PhotoEndpoints
{
    public static void MapPhotoEndpoints(this WebApplication app)
    {
        app.MapPut("/api/items/{id:guid}/photo", async (Guid id, HttpContext context, PhotoService photos, WardrobeService wardrobe) =>
        {
            var actor = CurrentActor.Id(context.User);
            wardrobe.GetItem(actor, id); // Check ownership before reading/buffering an upload.
            const int requestLimit = PhotoService.MaxBytes + 1024 * 1024;
            if (context.Request.ContentLength > requestLimit) throw new PhotoTooLargeException();
            if (!context.Request.HasFormContentType || context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
                return Results.Problem(statusCode: 400, title: "Expected multipart/form-data with one file field.");
            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = requestLimit }, context.RequestAborted);
            }
            catch (InvalidDataException) { return Results.Problem(statusCode: 400, title: "Invalid multipart form."); }
            if (form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "file")
                return Results.Problem(statusCode: 400, title: "Expected exactly one file field.");
            var file = form.Files[0];
            if (file.Length > PhotoService.MaxBytes) throw new PhotoTooLargeException();
            await using var stream = file.OpenReadStream();
            await photos.UploadAsync(actor, id, stream, file.ContentType, context.RequestAborted);
            return Results.Ok(WardrobeResponses.Item(wardrobe.GetItem(actor, id), $"/api/items/{id}/photo"));
        }).RequireAuthorization(CurrentActor.PolicyName).WithMetadata(new RequestSizeLimitAttribute(PhotoService.MaxBytes + 1024 * 1024))
            .AddEndpointFilter<CommitChangesFilter>();

        app.MapDelete("/api/items/{id:guid}/photo", (Guid id, HttpContext context, PhotoService photos) =>
        {
            photos.Delete(CurrentActor.Id(context.User), id);
            return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();

        app.MapGet("/api/items/{id:guid}/photo", (Guid id, HttpContext context, WardrobeService service, IPhotoStore store) =>
            Read(service.GetOwnedPhoto(CurrentActor.Id(context.User), id), context, store)).RequireAuthorization(CurrentActor.PolicyName);
        app.MapGet("/api/wardrobe/items/{id:guid}/photo", (Guid id, HttpContext context, WardrobeService service, IPhotoStore store) =>
            Read(service.GetPublicPhoto(id), context, store));
        app.MapGet("/api/wardrobe/outfits/{id:guid}/items/{itemId:guid}/photo", (Guid id, Guid itemId, HttpContext context, WardrobeService service, IPhotoStore store) =>
            Read(service.GetPublicOutfitPhoto(id, itemId), context, store));
        app.MapGet("/api/shared/items/{token}/photo", (string token, HttpContext context, WardrobeService service, IPhotoStore store) =>
            Read(service.GetSharedPhoto(token), context, store));
        app.MapGet("/api/shared/outfits/{token}/items/{itemId:guid}/photo", (string token, Guid itemId, HttpContext context, WardrobeService service, IPhotoStore store) =>
            Read(service.GetSharedPhoto(token, itemId), context, store));
    }

    private static async Task<IResult> Read(string? key, HttpContext context, IPhotoStore store)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (key is null) return Results.NotFound();
        var photo = await store.ReadAsync(key, context.RequestAborted);
        return photo is null ? Results.NotFound() : Results.Stream(photo.Content, photo.ContentType);
    }
}
