using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Outfitly.Api.Authentication;
using Outfitly.Application;
namespace Outfitly.Api.Endpoints;
public static class WorkspaceEndpoints
{
    public sealed record CopyRequest(Guid OperationId);
    public sealed record VersionRequest(Guid Version);
    public static void MapWorkspaceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization(CurrentActor.PolicyName);
        group.MapGet("/favorites", (string? kind, int? page, HttpContext h, WorkspaceService s) => s.Favorites(CurrentActor.Id(h.User), kind ?? "outfits", page ?? 1));
        foreach (var kind in new[] { "items", "outfits" })
        {
            group.MapGet($"/{kind}/{{id:guid}}/favorite", (Guid id, string? shareToken, HttpContext h, WorkspaceService s) => new { IsFavorite = s.IsFavorite(CurrentActor.Id(h.User), kind, id, shareToken) });
            group.MapPut($"/{kind}/{{id:guid}}/favorite", (Guid id, string? shareToken, HttpContext h, WorkspaceService s) => { s.Favorite(CurrentActor.Id(h.User), kind, id, shareToken, false); return Results.NoContent(); }).AddEndpointFilter<CommitChangesFilter>();
            group.MapDelete($"/{kind}/{{id:guid}}/favorite", (Guid id, HttpContext h, WorkspaceService s) => { s.Favorite(CurrentActor.Id(h.User), kind, id, null, true); return Results.NoContent(); }).AddEndpointFilter<CommitChangesFilter>();
        }
        group.MapPost("/outfits/{id:guid}/copies", async (Guid id, string? shareToken, CopyRequest request, HttpContext h, WorkspaceService s, CancellationToken ct) =>
            Results.Ok(new { Id = await s.CopyAsync(CurrentActor.Id(h.User), id, request.OperationId, shareToken, ct) })).AddEndpointFilter<CommitChangesFilter>();
        group.MapGet("/drafts", (int? page, HttpContext h, WorkspaceService s) => s.Drafts(CurrentActor.Id(h.User), page ?? 1));
        group.MapGet("/drafts/active", (string kind, Guid? targetId, HttpContext h, WorkspaceService s) => new { Draft = s.Active(CurrentActor.Id(h.User), kind, targetId) });
        group.MapGet("/drafts/{id:guid}", (Guid id, HttpContext h, WorkspaceService s) => s.Read(CurrentActor.Id(h.User), id));
        group.MapPut("/drafts/{id:guid}", (Guid id, DraftInput input, HttpContext h, WorkspaceService s) => s.Update(CurrentActor.Id(h.User), id, input)).AddEndpointFilter<CommitChangesFilter>();
        group.MapDelete("/drafts/{id:guid}", (Guid id, Guid version, HttpContext h, WorkspaceService s) => { s.Delete(CurrentActor.Id(h.User), id, version); return Results.NoContent(); }).AddEndpointFilter<CommitChangesFilter>();
        group.MapPost("/drafts/{id:guid}/save", (Guid id, VersionRequest input, HttpContext h, WorkspaceService s) => Results.Ok(new { Id = s.Save(CurrentActor.Id(h.User), id, input.Version) })).AddEndpointFilter<CommitChangesFilter>();
        group.MapGet("/drafts/{id:guid}/photo", async (Guid id, HttpContext h, WorkspaceService s, IPhotoStore files, CancellationToken ct) => {
            h.Response.Headers.CacheControl = "no-store"; h.Response.Headers.XContentTypeOptions = "nosniff";
            var key = s.Photo(CurrentActor.Id(h.User), id); var photo = key == null ? null : await files.ReadAsync(key, ct);
            return photo == null ? Results.NotFound() : Results.Stream(photo.Content, photo.ContentType);
        });
        group.MapPut("/drafts/{id:guid}/photo", async (Guid id, Guid version, HttpContext h, WorkspaceService s, CancellationToken ct) => {
            const int limit = PhotoService.MaxBytes + 1024 * 1024;
            if (h.Request.ContentLength > limit) throw new PhotoTooLargeException();
            if (h.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true) throw new ArgumentException("Expected multipart form.");
            IFormCollection form;
            try { form = await h.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = limit }, ct); }
            catch (InvalidDataException) { throw new ArgumentException("Invalid multipart form."); }
            if (form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "file") throw new ArgumentException("Expected one file field.");
            var file = form.Files[0]; if (file.Length > PhotoService.MaxBytes) throw new PhotoTooLargeException();
            await using var stream = file.OpenReadStream(); return await s.UploadAsync(CurrentActor.Id(h.User), id, version, stream, file.ContentType, ct);
        }).WithMetadata(new RequestSizeLimitAttribute(PhotoService.MaxBytes + 1024 * 1024)).AddEndpointFilter<CommitChangesFilter>();
        group.MapDelete("/drafts/{id:guid}/photo", (Guid id, Guid version, HttpContext h, WorkspaceService s) => s.RemovePhoto(CurrentActor.Id(h.User), id, version)).AddEndpointFilter<CommitChangesFilter>();
    }
}
