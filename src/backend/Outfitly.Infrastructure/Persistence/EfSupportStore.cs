using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence;
public sealed class EfSupportStore(OutfitlyDbContext context) : ISupportStore
{
    public Task<SupportTicket?> FindAsync(Guid id, CancellationToken token) => context.Set<SupportTicket>()
        .Include(x => x.Messages).ThenInclude(x => x.Attachments).SingleOrDefaultAsync(x => x.Id == id, token);
    public void Add(SupportTicket ticket) => context.Add(ticket);
    public async Task<SupportPage> ListAsync(Guid? owner, string? category, string? status, int page, CancellationToken token)
    {
        var query = context.Set<SupportTicket>().AsNoTracking();
        if (owner is { } actor) query = query.Where(x => x.OwnerId == actor);
        if (category is not null) query = query.Where(x => x.Category == category);
        if (status is not null) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20)
            .Select(x => new SupportSummary(x.Id, x.Subject, x.Category, x.Status, x.UpdatedAtUtc)).ToArrayAsync(token);
        return new(items, total, page);
    }
}
