using Outfitly.Domain;

namespace Outfitly.Application;

public sealed record ItemCatalogQuery(string? Search = null, ClothingCategory? Category = null,
    string? Color = null, string Sort = "nameAsc", int Page = 1, int PageSize = 20)
{
    public ItemCatalogQuery Normalize()
    {
        if (Page < 1 || PageSize is < 1 or > 100 || (long)(Page - 1) * PageSize > int.MaxValue)
            throw new ArgumentException("Invalid page or pageSize (1–100).");
        if (Sort is not ("nameAsc" or "nameDesc")) throw new ArgumentException("Invalid sort.");
        if (Category is { } category && !Enum.IsDefined(category)) throw new ArgumentException("Invalid category.");
        return this with { Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Color = string.IsNullOrWhiteSpace(Color) ? null : Color.Trim() };
    }
}

public sealed record ItemPage(IReadOnlyCollection<ItemView> Items, int Total, int Page, int PageSize);

public interface IItemCatalogQuery
{
    Task<ItemPage> GetAsync(Guid? ownerId, ItemCatalogQuery query, CancellationToken cancellationToken);
}

public sealed class ItemCatalog(IItemCatalogQuery query)
{
    public Task<ItemPage> GetMyItemsAsync(Guid actor, ItemCatalogQuery filter, CancellationToken cancellationToken)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException();
        return query.GetAsync(actor, filter.Normalize(), cancellationToken);
    }
    public Task<ItemPage> GetPublicItemsAsync(ItemCatalogQuery filter, CancellationToken cancellationToken) =>
        query.GetAsync(null, filter.Normalize(), cancellationToken);
}
