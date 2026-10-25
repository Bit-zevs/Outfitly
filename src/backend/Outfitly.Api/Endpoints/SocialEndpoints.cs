using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Outfitly.Api.Authentication;
using Outfitly.Application;
namespace Outfitly.Api.Endpoints;
public static class SocialEndpoints
{
    public static void MapSocialEndpoints(this WebApplication app)
    {
        app.MapGet("/api/users/{username}/followers", (string username, int? page, SocialProfiles profiles, CancellationToken ct) => profiles.ConnectionsAsync(username, true, page ?? 1, ct));
        app.MapGet("/api/users/{username}/following", (string username, int? page, SocialProfiles profiles, CancellationToken ct) => profiles.ConnectionsAsync(username, false, page ?? 1, ct));
        app.MapPut("/api/users/{username}/follow", async (string username, HttpContext http, SocialProfiles profiles, CancellationToken ct) => {
            await profiles.FollowAsync(CurrentActor.Id(http.User), username, false, ct); return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
        app.MapDelete("/api/users/{username}/follow", async (string username, HttpContext http, SocialProfiles profiles, CancellationToken ct) => {
            await profiles.FollowAsync(CurrentActor.Id(http.User), username, true, ct); return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
        app.MapGet("/api/users/{id:guid}/avatar", async (Guid id, HttpContext http, SocialProfiles profiles, IPhotoStore store, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store"; http.Response.Headers.XContentTypeOptions = "nosniff";
            var key = await profiles.AvatarKeyAsync(id, ct);
            var photo = key is null ? null : await store.ReadAsync(key, ct);
            return photo is null ? Results.NotFound() : Results.Stream(photo.Content, photo.ContentType);
        });
        app.MapPut("/api/auth/me/avatar", async (HttpContext http, AvatarService avatars, CancellationToken ct) => {
            const int requestLimit = PhotoService.MaxBytes + 1024 * 1024;
            if (http.Request.ContentLength > requestLimit) throw new PhotoTooLargeException();
            if (http.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true) throw new ArgumentException("Expected multipart form.");
            IFormCollection form;
            try { form = await http.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = requestLimit }, ct); }
            catch (InvalidDataException) { throw new ArgumentException("Invalid multipart form."); }
            if (form.Files.Count != 1 || form.Files[0].Name != "file" || form.Count != 3) throw new ArgumentException("Expected file and cropX, cropY, cropSize.");
            int Coordinate(string name) => form[name].Count == 1 && int.TryParse(form[name], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw new ArgumentException("Invalid crop coordinate.");
            var crop = new AvatarCrop(Coordinate("cropX"), Coordinate("cropY"), Coordinate("cropSize"));
            var file = form.Files[0]; if (file.Length > PhotoService.MaxBytes) throw new PhotoTooLargeException();
            await using var stream = file.OpenReadStream();
            await avatars.UploadAsync(CurrentActor.Id(http.User), stream, file.ContentType, crop, ct);
            return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName).WithMetadata(new RequestSizeLimitAttribute(PhotoService.MaxBytes + 1024 * 1024)).AddEndpointFilter<CommitChangesFilter>();
        app.MapDelete("/api/auth/me/avatar", async (HttpContext http, AvatarService avatars, CancellationToken ct) => {
            await avatars.DeleteAsync(CurrentActor.Id(http.User), ct); return Results.NoContent();
        }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
    }
}
