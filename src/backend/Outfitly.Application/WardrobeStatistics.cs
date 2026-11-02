namespace Outfitly.Application;

public sealed record StatisticsGroup(string? Label, int Count);
public sealed record CategoryStatistics(Outfitly.Domain.ClothingCategory Category, int Count);
public sealed record ReactionStatistics(int Views, int Likes);
public sealed record WardrobeStatisticsView(int Items, int Outfits, int PublicItems, int PublicOutfits,
    int ItemsWithoutOutfits, IReadOnlyCollection<CategoryStatistics> Categories,
    IReadOnlyCollection<StatisticsGroup> Colors, IReadOnlyCollection<StatisticsGroup> Brands,
    ReactionStatistics ItemReactions, ReactionStatistics OutfitReactions);
public interface IWardrobeStatisticsQuery
{
    Task<WardrobeStatisticsView> GetAsync(Guid owner, CancellationToken cancellationToken);
}
public sealed class WardrobeStatistics(IWardrobeStatisticsQuery query)
{
    public Task<WardrobeStatisticsView> GetAsync(Guid actor, CancellationToken cancellationToken)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException();
        return query.GetAsync(actor, cancellationToken);
    }
}
