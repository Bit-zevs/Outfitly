using Outfitly.Api.Authentication;
using Outfitly.Application;
namespace Outfitly.Api.Endpoints;
public static class EngagementEndpoints
{
    public sealed record CommentRequest(string Text, Guid? ParentId);
    public static void MapEngagementEndpoints(this WebApplication app)
    {
        foreach (var kind in new[] { "items", "outfits" })
        {
            var outfit = kind == "outfits";
            var group = app.MapGroup($"/api/{kind}/{{id:guid}}");
            group.MapGet("/engagement", (Guid id, string? shareToken, HttpContext http, PostEngagement service) =>
                service.Read(id, outfit, Actor(http), shareToken));
            group.MapGet("/comments", (Guid id, string? shareToken, Guid? parentId, int? page, HttpContext http, PostEngagement service) =>
                service.Comments(id, outfit, Actor(http), shareToken, parentId, page ?? 1));
            group.MapPost("/views", (Guid id, string? shareToken, HttpContext http, PostEngagement service) => {
                service.React(id, outfit, CurrentActor.Id(http.User), shareToken, "view"); return Results.NoContent();
            }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
            group.MapPut("/like", (Guid id, string? shareToken, HttpContext http, PostEngagement service) => {
                service.React(id, outfit, CurrentActor.Id(http.User), shareToken, "like"); return Results.NoContent();
            }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
            group.MapDelete("/like", (Guid id, string? shareToken, HttpContext http, PostEngagement service) => {
                service.React(id, outfit, CurrentActor.Id(http.User), shareToken, "like", true); return Results.NoContent();
            }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
            group.MapPost("/comments", (Guid id, string? shareToken, CommentRequest request, HttpContext http, PostEngagement service) =>
                Results.Ok(new { Id = service.AddComment(id, outfit, CurrentActor.Id(http.User), shareToken, request.Text, request.ParentId) }))
                .RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
            group.MapPatch("/comments/{commentId:guid}", (Guid id, Guid commentId, string? shareToken, CommentRequest request, HttpContext http, PostEngagement service) => {
                service.ChangeComment(id, outfit, CurrentActor.Id(http.User), shareToken, commentId, request.Text, false); return Results.NoContent();
            }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
            group.MapDelete("/comments/{commentId:guid}", (Guid id, Guid commentId, string? shareToken, HttpContext http, PostEngagement service) => {
                service.ChangeComment(id, outfit, CurrentActor.Id(http.User), shareToken, commentId, null, true); return Results.NoContent();
            }).RequireAuthorization(CurrentActor.PolicyName).AddEndpointFilter<CommitChangesFilter>();
        }
    }
    private static Guid? Actor(HttpContext http) => http.User.Identity?.IsAuthenticated == true ? CurrentActor.Id(http.User) : null;
}
