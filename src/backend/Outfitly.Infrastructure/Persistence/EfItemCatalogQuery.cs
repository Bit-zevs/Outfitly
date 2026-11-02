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
            items = items.Where(item => item.Color != null && (ownerId == null ? item.Color.ToLower() : item.Color.Trim().ToLower()) == normalized);
        }
        if (ownerId.HasValue)
        {
            if (query.Brand is { } brand) { var value = brand.ToLowerInvariant(); items = items.Where(x => x.Brand != null && x.Brand.Trim().ToLower() == value); }
            if (query.Size is { } size) { var value = size.ToLowerInvariant(); items = items.Where(x => x.Size != null && x.Size.Trim().ToLower() == value); }
            if (query.ColorMissing) items = items.Where(x => x.Color == null || x.Color.Trim() == "");
            if (query.BrandMissing) items = items.Where(x => x.Brand == null || x.Brand.Trim() == "");
            if (query.SizeMissing) items = items.Where(x => x.Size == null || x.Size.Trim() == "");
            if (query.Publication != "all") items = items.Where(x => x.IsPublic == (query.Publication == "public"));
        }
        if (query.Search is { } search)
        {
            if (context.Database.IsNpgsql())
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                items = items.Where(item => EF.Functions.ILike(item.Name, pattern, "\\") || ownerId.HasValue &&
                    (item.Brand != null && EF.Functions.ILike(item.Brand, pattern, "\\") || item.Description != null && EF.Functions.ILike(item.Description, pattern, "\\")));
            }
            else
            {
                var normalized = search.ToLowerInvariant();
                items = items.Where(item => item.Name.ToLower().Contains(normalized) || ownerId.HasValue &&
                    (item.Brand != null && item.Brand.ToLower().Contains(normalized) || item.Description != null && item.Description.ToLower().Contains(normalized)));
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
    public async Task<ItemFilterOptions> GetFilterOptionsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var items = context.WardrobeItems.AsNoTracking().Where(x => x.OwnerId == ownerId);
        return new(await Options(items.Select(x => x.Color), cancellationToken),
            await Options(items.Select(x => x.Brand), cancellationToken),
            await Options(items.Select(x => x.Size), cancellationToken));
    }
    private static async Task<string?[]> Options(IQueryable<string?> values, CancellationToken token)
    {
        var options = await values.Select(x => x == null || x.Trim() == "" ? null : x.Trim().ToLower()).Distinct().ToArrayAsync(token);
        return options.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }}
