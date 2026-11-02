using Outfitly.Domain;

namespace Outfitly.Application;

public sealed record ItemCatalogQuery(string? Search = null, ClothingCategory? Category = null,
    string? Color = null, string Sort = "nameAsc", int Page = 1, int PageSize = 20,
    string? Brand = null, string? Size = null, string Publication = "all",
    bool ColorMissing = false, bool BrandMissing = false, bool SizeMissing = false)
{
    public ItemCatalogQuery Normalize()
    {
        if (Page < 1 || PageSize is < 1 or > 100 || (long)(Page - 1) * PageSize > int.MaxValue)
            throw new ArgumentException("Invalid page or pageSize (1–100).");
        if (Sort is not ("nameAsc" or "nameDesc")) throw new ArgumentException("Invalid sort.");
        if (Publication is not ("all" or "public" or "private")) throw new ArgumentException("Invalid publication.");
        if (Category is { } category && !Enum.IsDefined(category)) throw new ArgumentException("Invalid category.");
        if (ColorMissing && Color is not null || BrandMissing && Brand is not null || SizeMissing && Size is not null)
            throw new ArgumentException("A value and missing flag cannot be combined.");
        return this with { Search = Clean(Search), Color = Clean(Color), Brand = Clean(Brand), Size = Clean(Size) };
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ItemPage(IReadOnlyCollection<ItemView> Items, int Total, int Page, int PageSize);
public sealed record ItemFilterOptions(IReadOnlyCollection<string?> Colors, IReadOnlyCollection<string?> Brands,
    IReadOnlyCollection<string?> Sizes);

public interface IItemCatalogQuery
{
    Task<ItemPage> GetAsync(Guid? ownerId, ItemCatalogQuery query, CancellationToken cancellationToken);
    Task<ItemFilterOptions> GetFilterOptionsAsync(Guid ownerId, CancellationToken cancellationToken);
}

public sealed class ItemCatalog(IItemCatalogQuery query)
{
    public Task<ItemPage> GetMyItemsAsync(Guid actor, ItemCatalogQuery filter, CancellationToken cancellationToken)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException();
        return query.GetAsync(actor, filter.Normalize(), cancellationToken);
    }
    public Task<ItemFilterOptions> GetFilterOptionsAsync(Guid actor, CancellationToken cancellationToken)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException();
        return query.GetFilterOptionsAsync(actor, cancellationToken);
    }
    public Task<ItemPage> GetPublicItemsAsync(ItemCatalogQuery filter, CancellationToken cancellationToken) =>
        query.GetAsync(null, filter.Normalize(), cancellationToken);
}
