using System.Globalization;
using Outfitly.Application;
using Outfitly.Api.Authentication;
using Outfitly.Api.Contracts;
namespace Outfitly.Api.Endpoints;
public static class CalendarEndpoints
{
    private static DateOnly Date(string value, string format) => DateOnly.TryParseExact(value, format,
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new ArgumentException("Invalid calendar date.");
    public static void MapCalendarEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/calendar").RequireAuthorization(CurrentActor.PolicyName);
        group.MapGet("/", (HttpContext http, OutfitCalendar calendar) =>
        {
            var month = http.Request.Query["month"];
            if (month.Count != 1) throw new ArgumentException("Provide one month.");
            return calendar.ReadMonth(CurrentActor.Id(http.User), Date(month[0] + "-01", "yyyy-MM-dd"))
                .Select(x => new { x.Date, Outfit = WardrobeResponses.Outfit(x.Outfit, id => $"/api/items/{id}/photo") });
        });
        group.MapPut("/{date}/outfits/{outfitId:guid}", (string date, Guid outfitId, HttpContext http, OutfitCalendar calendar) =>
        {
            calendar.Set(CurrentActor.Id(http.User), Date(date, "yyyy-MM-dd"), outfitId, true);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
        group.MapDelete("/{date}/outfits/{outfitId:guid}", (string date, Guid outfitId, HttpContext http, OutfitCalendar calendar) =>
        {
            calendar.Set(CurrentActor.Id(http.User), Date(date, "yyyy-MM-dd"), outfitId, false);
            return Results.NoContent();
        }).AddEndpointFilter<CommitChangesFilter>();
    }
}
