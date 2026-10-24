using Microsoft.EntityFrameworkCore;
using Outfitly.Application;

namespace Outfitly.Infrastructure.Persistence;

public sealed class EfItemCatalogQuery(OutfitlyDbContext context) : IItemCatalogQuery
{
    public async Task<ItemPage> GetAsync(Guid? ownerId, ItemCatalogQuery query, CancellationToken cancellationToken)
    {
        query = query.Normalize();
        var items = context.WardrobeItems.AsNoTracking();
        items = ownerId is { } owner ? items.Where(item => item.OwnerId == owner) : items.Where(item => item.IsPublic);
        if (query.Category is { } category) items = items.Where(item => item.Category == category);
        if (query.Color is { } color)
        {
            var normalized = color.ToLowerInvariant();
            items = items.Where(item => item.Color != null && item.Color.ToLower() == normalized);
        }
        if (query.Search is { } search)
        {
            if (context.Database.IsNpgsql())
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                items = items.Where(item => EF.Functions.ILike(item.Name, pattern, "\\"));
            }
            else
            {
                var normalized = search.ToLowerInvariant();
                items = items.Where(item => item.Name.ToLower().Contains(normalized));
            }
        }
        var total = await items.CountAsync(cancellationToken);
        var ordered = query.Sort == "nameDesc" ? items.OrderByDescending(item => item.Name) : items.OrderBy(item => item.Name);
        var page = await ordered.ThenBy(item => item.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(item => new ItemView(item.Id, item.OwnerId, item.Name, item.Category, item.Color, item.Size,
                item.Brand, item.Description, item.PhotoUrl, item.IsPublic, item.PhotoStorageKey != null,
                context.Users.Where(user => user.Id == item.OwnerId).Select(user => user.Username).Single()))
            .ToArrayAsync(cancellationToken);
        return new(page, total, query.Page, query.PageSize);
    }
}
