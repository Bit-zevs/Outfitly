using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence;
public sealed class EfOutfitCalendarStore(OutfitlyDbContext context) : IOutfitCalendarStore
{
    public IReadOnlyCollection<OutfitAssignment> ReadMonth(Guid owner, DateOnly month)
    {
        var last = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
        return context.Set<OutfitAssignment>().AsNoTracking().Where(x => x.OwnerId == owner && x.Date >= month && x.Date <= last)
            .OrderBy(x => x.Date).ThenBy(x => x.OutfitId).ToArray();
    }
    public OutfitAssignment? Find(Guid owner, DateOnly date, Guid outfit) => context.Set<OutfitAssignment>().Find(owner, date, outfit);
    public void Add(OutfitAssignment assignment) => context.Add(assignment);
    public void Remove(OutfitAssignment assignment) => context.Remove(assignment);
}
