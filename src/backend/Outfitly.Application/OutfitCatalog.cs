using Outfitly.Domain;
namespace Outfitly.Application;
public interface IOutfitCatalogQuery
{
    Task<PublicPage<OutfitView>> MineAsync(Guid owner, ItemCatalogQuery filter, CancellationToken cancellationToken);
}
public sealed class OutfitCatalog(IOutfitCatalogQuery query)
{
    public Task<PublicPage<OutfitView>> GetAsync(Guid actor, ItemCatalogQuery filter, CancellationToken ct)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException();
        return query.MineAsync(actor, filter.Normalize(), ct);
    }
}
