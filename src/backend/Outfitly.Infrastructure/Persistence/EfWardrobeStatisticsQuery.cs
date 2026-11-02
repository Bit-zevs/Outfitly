using System.Data;
using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence;

public sealed class EfWardrobeStatisticsQuery(OutfitlyDbContext context) : IWardrobeStatisticsQuery
{
    public async Task<WardrobeStatisticsView> GetAsync(Guid owner, CancellationToken cancellationToken)
    {
        // PostgreSQL ReadCommitted takes a new snapshot per statement.
        await using var transaction = await context.Database.BeginTransactionAsync(
            context.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken);
        var items = context.WardrobeItems.AsNoTracking();
        items = items.Where(x => x.OwnerId == owner);
        var outfits = context.Outfits.AsNoTracking().IgnoreAutoIncludes().Where(x => x.OwnerId == owner);
        var categories = await items.GroupBy(x => x.Category)
            .Select(x => new CategoryStatistics(x.Key, x.Count())).ToArrayAsync(cancellationToken);
        var colors = await Groups(items.Select(x => x.Color), cancellationToken);
        var brands = await Groups(items.Select(x => x.Brand), cancellationToken);
        var membership = context.Set<Dictionary<string, object>>("OutfitItems");
        var unused = await items.CountAsync(x => !membership.Any(m =>
            EF.Property<Guid>(m, "OwnerId") == owner && EF.Property<Guid>(m, "WardrobeItemId") == x.Id), cancellationToken);
        var reactions = context.Set<PostInteraction>().AsNoTracking();
        var itemReactions = await Reactions(reactions.Where(r => items.Any(i => i.Id == r.ItemId)), cancellationToken);
        var outfitReactions = await Reactions(reactions.Where(r => outfits.Any(o => o.Id == r.OutfitId)), cancellationToken);
        var result = new WardrobeStatisticsView(await items.CountAsync(cancellationToken), await outfits.CountAsync(cancellationToken),
            await items.CountAsync(x => x.IsPublic, cancellationToken), await outfits.CountAsync(x => x.IsPublic, cancellationToken),
            unused, Enum.GetValues<ClothingCategory>().Select(c => new CategoryStatistics(c,
                categories.FirstOrDefault(x => x.Category == c)?.Count ?? 0)).ToArray(), colors, brands, itemReactions, outfitReactions);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<StatisticsGroup[]> Groups(IQueryable<string?> values, CancellationToken token)
    {
        var groups = await values.GroupBy(x => x == null || x.Trim() == "" ? null : x.Trim().ToLower())
            .Select(g => new StatisticsGroup(g.Key, g.Count())).ToArrayAsync(token);
        return groups.OrderByDescending(x => x.Count).ThenBy(x => x.Label, StringComparer.Ordinal).ToArray();
    }

    private static async Task<ReactionStatistics> Reactions(IQueryable<PostInteraction> query, CancellationToken token)
    {
        var counts = await query.Where(x => x.Kind == "view" || x.Kind == "like").GroupBy(x => x.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() }).ToArrayAsync(token);
        return new(counts.FirstOrDefault(x => x.Kind == "view")?.Count ?? 0,
            counts.FirstOrDefault(x => x.Kind == "like")?.Count ?? 0);
    }
}
