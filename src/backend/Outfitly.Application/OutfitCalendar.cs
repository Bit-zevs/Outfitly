using Outfitly.Domain;
namespace Outfitly.Application;

public interface IOutfitCalendarStore
{
    IReadOnlyCollection<OutfitAssignment> ReadMonth(Guid owner, DateOnly month);
    OutfitAssignment? Find(Guid owner, DateOnly date, Guid outfit);
    void Add(OutfitAssignment assignment);
    void Remove(OutfitAssignment assignment);
}
public sealed record CalendarEntry(DateOnly Date, OutfitView Outfit);
public sealed class OutfitCalendar(IOutfitCalendarStore store, IWardrobeRepository wardrobe, WardrobeService service)
{
    public IReadOnlyCollection<CalendarEntry> ReadMonth(Guid actor, DateOnly month)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException();
        var assignments = store.ReadMonth(actor, new DateOnly(month.Year, month.Month, 1));
        if (assignments.Count == 0) return [];
        var outfits = service.GetMyOutfits(actor).ToDictionary(x => x.Id);
        return assignments.Where(x => outfits.ContainsKey(x.OutfitId)).Select(x => new CalendarEntry(x.Date, outfits[x.OutfitId])).ToArray();
    }
    public void Set(Guid actor, DateOnly date, Guid outfitId, bool assigned)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException();
        var outfit = wardrobe.GetOutfits().SingleOrDefault(x => x.Id == outfitId) ?? throw new KeyNotFoundException();
        if (outfit.OwnerId != actor) throw new UnauthorizedAccessException();
        wardrobe.MarkOutfitChanged(outfit);
        var existing = store.Find(actor, date, outfitId);
        if (assigned && existing is null) store.Add(new OutfitAssignment(actor, date, outfitId));
        if (!assigned && existing is not null) store.Remove(existing);
    }
}
