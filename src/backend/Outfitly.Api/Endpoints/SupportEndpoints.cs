using Microsoft.AspNetCore.Mvc;
using Outfitly.Application;
using Outfitly.Api.Authentication;
namespace Outfitly.Api.Endpoints;
public static class SupportEndpoints
{
    public sealed record StatusRequest(Guid Revision, string Status);
    public static void MapSupportEndpoints(this WebApplication app)
    {
        foreach (var admin in new[] { false, true })
        {
            var group = app.MapGroup(admin ? "/api/admin/support/tickets" : "/api/support/tickets").RequireAuthorization(CurrentActor.PolicyName);
            group.MapGet("/", (HttpContext http, SupportService service) => service.ListAsync(CurrentActor.Id(http.User), admin,
                Single(http, "category"), Single(http, "status"), int.TryParse(Single(http, "page") ?? "1", out var page) ? page : throw new ArgumentException("Invalid page."), http.RequestAborted));
            group.MapGet("/{id:guid}", (Guid id, HttpContext http, SupportService service) => service.ReadAsync(CurrentActor.Id(http.User), id, admin, http.RequestAborted));
            group.MapPost("/{id:guid}/messages", async (Guid id, HttpContext http, SupportService service) =>
            {
                if (admin) service.RequireAdmin(CurrentActor.Id(http.User));
                var form = await Form(http); var uploads = await Uploads(form, http.RequestAborted);
                if (!Guid.TryParse(Value(form, "revision"), out var revision)) throw new ArgumentException("Invalid revision.");
                await service.ReplyAsync(CurrentActor.Id(http.User), id, admin, revision, Value(form, "text"), uploads, http.RequestAborted);
                return Results.NoContent();
            }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024 * 1024)).RequireRateLimiting("support-messages").AddEndpointFilter<CommitChangesFilter>();
            if (admin)
                group.MapPatch("/{id:guid}/status", async (Guid id, StatusRequest request, HttpContext http, SupportService service) =>
                { await service.SetStatusAsync(CurrentActor.Id(http.User), id, request.Revision, request.Status, http.RequestAborted); return Results.NoContent(); }).AddEndpointFilter<CommitChangesFilter>();
        }
        app.MapPost("/api/support/tickets", async (HttpContext http, SupportService service) =>
        {
            var form = await Form(http);
            var ticket = await service.CreateAsync(CurrentActor.Id(http.User), Value(form, "category"), Value(form, "subject"), Value(form, "text"), await Uploads(form, http.RequestAborted), http.RequestAborted);
            return Results.Created($"/api/support/tickets/{ticket.Id}", ticket);
        }).RequireAuthorization(CurrentActor.PolicyName).RequireRateLimiting("support-create")
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024 * 1024)).AddEndpointFilter<CommitChangesFilter>();
        app.MapGet("/api/support/tickets/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, HttpContext http, SupportService service) =>
        {
            http.Response.Headers.CacheControl = "no-store"; http.Response.Headers.XContentTypeOptions = "nosniff";
            var photo = await service.AttachmentAsync(CurrentActor.Id(http.User), id, attachmentId, http.RequestAborted);
            return photo is null ? Results.NotFound() : Results.File(photo.Content, photo.ContentType);
        }).RequireAuthorization(CurrentActor.PolicyName);
    }
    private static string? Single(HttpContext http, string name)
    {
        var values = http.Request.Query[name]; if (values.Count > 1) throw new ArgumentException("Repeated parameter.");
        return values.Count == 0 ? null : values[0];
    }
    private static string Value(IFormCollection form, string name)
    { var values = form[name]; if (values.Count > 1) throw new ArgumentException("Repeated field."); return values.ToString(); }
    private static async Task<IFormCollection> Form(HttpContext http)
    { if (!http.Request.HasFormContentType || !http.Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use multipart/form-data."); return await http.Request.ReadFormAsync(http.RequestAborted); }
    private static async Task<IReadOnlyCollection<SupportUpload>> Uploads(IFormCollection form, CancellationToken token)
    {
        if (form.Files.Count > 3) throw new ArgumentException("At most three images.");
        var uploads = new List<SupportUpload>();
        foreach (var file in form.Files)
        {
            if (file.Length > PhotoService.MaxBytes) throw new PhotoTooLargeException();
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, token); uploads.Add(new(buffer.ToArray(), file.ContentType));
        }
        return uploads;
    }
}
