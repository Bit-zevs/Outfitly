using Outfitly.Domain;
using Microsoft.EntityFrameworkCore;
using Outfitly.Application;

namespace Outfitly.Infrastructure.Persistence;

public sealed class EfItemCatalogQuery(OutfitlyDbContext context) : IItemCatalogQuery
{
    public async Task<ItemPage> GetAsync(Guid? ownerId, ItemCatalogQuery query, CancellationToken cancellationToken)
    {
        query = query.Normalize();
        var items = ApplyAttributes(VisibleItems(ownerId), query);
        if (ownerId.HasValue && query.Publication != "all")
            items = items.Where(item => item.IsPublic == (query.Publication == "public"));
        if (query.Search is { } search)
        {
            if (context.Database.IsNpgsql())
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                items = items.Where(item => EF.Functions.ILike(item.Name, pattern, "\\") ||
                    item.Brand != null && EF.Functions.ILike(item.Brand, pattern, "\\") ||
                    item.Description != null && EF.Functions.ILike(item.Description, pattern, "\\"));
            }
            else
            {
                var normalized = search.ToLowerInvariant();
                items = items.Where(item => item.Name.ToLower().Contains(normalized) ||
                    item.Brand != null && item.Brand.ToLower().Contains(normalized) ||
                    item.Description != null && item.Description.ToLower().Contains(normalized));
            }
        }
        var total = await items.CountAsync(cancellationToken);
        var ordered = query.Sort switch
        {
            "nameDesc" => items.OrderByDescending(item => item.Name),
            "newest" => items.OrderByDescending(item => item.CreatedAtUtc.HasValue).ThenByDescending(item => item.CreatedAtUtc),
            "likes" => items.OrderByDescending(item => context.Set<PostInteraction>().Count(reaction => reaction.ItemId == item.Id && reaction.Kind == "like")),
            "views" => items.OrderByDescending(item => context.Set<PostInteraction>().Count(reaction => reaction.ItemId == item.Id && reaction.Kind == "view")),
            _ => items.OrderBy(item => item.Name)
        };
        var page = await ordered.ThenBy(item => item.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(item => new ItemView(item.Id, item.OwnerId, item.Name, item.Category, item.Color, item.Size,
                item.Brand, item.Description, item.PhotoUrl, item.IsPublic, item.PhotoStorageKey != null,
                context.Users.Where(user => user.Id == item.OwnerId).Select(user => user.Username).Single()))
            .ToArrayAsync(cancellationToken);
        return new(page, total, query.Page, query.PageSize);
    }
    internal static IQueryable<WardrobeItem> ApplyAttributes(IQueryable<WardrobeItem> items, ItemCatalogQuery filter)
    {
        if (filter.Category is { } category) items = items.Where(item => item.Category == category);
        if (filter.Color is { } color)
        {
            var value = color.ToLowerInvariant();
            items = items.Where(item => item.Color != null && item.Color.Trim().ToLower() == value);
        }
        if (filter.Brand is { } brand)
        {
            var value = brand.ToLowerInvariant();
            items = items.Where(item => item.Brand != null && item.Brand.Trim().ToLower() == value);
        }
        if (filter.Size is { } size)
        {
            var value = size.ToLowerInvariant();
            items = items.Where(item => item.Size != null && item.Size.Trim().ToLower() == value);
        }
        if (filter.ColorMissing) items = items.Where(item => item.Color == null || item.Color.Trim() == "");
        if (filter.BrandMissing) items = items.Where(item => item.Brand == null || item.Brand.Trim() == "");
        if (filter.SizeMissing) items = items.Where(item => item.Size == null || item.Size.Trim() == "");
        return items;
    }

    private IQueryable<WardrobeItem> VisibleItems(Guid? ownerId)
    {
        var items = context.WardrobeItems.AsNoTracking();
        return ownerId is { } owner ? items.Where(item => item.OwnerId == owner) : items.Where(item => item.IsPublic);
    }

    public Task<ItemFilterOptions> GetFilterOptionsAsync(Guid? ownerId, CancellationToken cancellationToken) =>
        ReadOptions(VisibleItems(ownerId), cancellationToken);

    public Task<ItemFilterOptions> GetPublicOutfitFilterOptionsAsync(CancellationToken cancellationToken) =>
        ReadOptions(context.Outfits.AsNoTracking().Where(outfit => outfit.IsPublic)
            .SelectMany(outfit => EF.Property<List<WardrobeItem>>(outfit, "_items")), cancellationToken);

    private static async Task<ItemFilterOptions> ReadOptions(IQueryable<WardrobeItem> items, CancellationToken token) =>
        new(await Options(items.Select(item => item.Color), token),
            await Options(items.Select(item => item.Brand), token),
            await Options(items.Select(item => item.Size), token));

    private static async Task<string?[]> Options(IQueryable<string?> values, CancellationToken token)
    {
        var options = await values.Select(x => x == null || x.Trim() == "" ? null : x.Trim().ToLower()).Distinct().ToArrayAsync(token);
        return options.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}
