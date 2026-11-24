using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence;

/// <summary>
/// Tracks a unit of work. The caller commits the complete use case with DbContext.SaveChangesAsync.
/// </summary>
public sealed class EfWardrobeRepository(OutfitlyDbContext context) : IWardrobeRepository
{
    public WardrobeItem? FindItemForChange(Guid id) => context.WardrobeItems.Find(id);

    public ShareLink? FindLinkForChange(Guid id) => context.ShareLinks.Find(id);

    public IReadOnlyCollection<ShareLink> FindTargetLinks(ShareTargetType type, Guid id) =>
        ReadTracked(context.ShareLinks, link => link.TargetType == type && link.TargetId == id);

    public IReadOnlyCollection<Outfit> FindOutfitsContaining(Guid ownerId, Guid itemId)
    {
        var outfits = context.Outfits;
        outfits.Where(outfit => outfit.OwnerId == ownerId &&
            EF.Property<List<WardrobeItem>>(outfit, "_items").Any(item => item.Id == itemId)).Load();
        return outfits.Local.Where(outfit => outfit.OwnerId == ownerId && outfit.ItemIds.Contains(itemId)).ToArray();
    }

    public IReadOnlyCollection<WardrobeItem> FindItemsForChange(IReadOnlyCollection<Guid> ids) =>
        ids.Count == 0 ? [] : ReadTracked(context.WardrobeItems, item => ids.Contains(item.Id));

    public IReadOnlyCollection<WardrobeItem> FindItems(Guid? ownerId = null, bool publicOnly = false) =>
        ReadTracked(context.WardrobeItems, item => (ownerId == null || item.OwnerId == ownerId) && (!publicOnly || item.IsPublic));

    public IReadOnlyCollection<Outfit> FindOutfits(Guid? ownerId = null, bool publicOnly = false) =>
        ReadTracked(context.Outfits, outfit => (ownerId == null || outfit.OwnerId == ownerId) && (!publicOnly || outfit.IsPublic));

    public ShareLink? FindActiveLink(string token, ShareTargetType targetType) =>
        context.ShareLinks.AsNoTracking().SingleOrDefault(link =>
            link.Token == token && link.TargetType == targetType && link.IsActive);

    public WardrobeItem? ReadItem(Guid id, bool publicOnly = false) =>
        context.WardrobeItems.AsNoTracking().SingleOrDefault(item => item.Id == id && (!publicOnly || item.IsPublic));

    public Outfit? ReadOutfit(Guid id, bool publicOnly = false) =>
        context.Outfits.AsNoTracking().SingleOrDefault(outfit => outfit.Id == id && (!publicOnly || outfit.IsPublic));

    public Outfit? FindOutfitForChange(Guid id) =>
        context.Outfits.Local.SingleOrDefault(outfit => outfit.Id == id) ??
        ReadTracked(context.Outfits, outfit => outfit.Id == id).SingleOrDefault();

    public IReadOnlyCollection<WardrobeItem> ReadItems(IReadOnlyCollection<Guid> ids) =>
        context.WardrobeItems.AsNoTracking().Where(item => ids.Contains(item.Id)).ToArray();

    public void MarkOutfitChanged(Outfit outfit)
    {
        // Preserve OriginalValue from the first tracked read. Even link-only and
        // membership-only commands must update the parent row in the same transaction.
        var entry = context.Entry(outfit);
        if (entry.State == EntityState.Detached)
            throw new InvalidOperationException("Read the outfit in this unit of work before changing it.");
        entry.Property<Guid>("Revision").CurrentValue = Guid.NewGuid();
    }

    public void Add(WardrobeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        context.WardrobeItems.Add(item);
    }

    public void Add(Outfit outfit)
    {
        ArgumentNullException.ThrowIfNull(outfit);
        context.Outfits.Add(outfit);
    }

    public void Add(ShareLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        context.ShareLinks.Add(link);
    }

    public void Remove(WardrobeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        context.WardrobeItems.Remove(item);
    }

    public void Remove(Outfit outfit)
    {
        ArgumentNullException.ThrowIfNull(outfit);
        context.Outfits.Remove(outfit);
    }

    private static IReadOnlyCollection<TEntity> ReadTracked<TEntity>(DbSet<TEntity> set,
        Expression<Func<TEntity, bool>> predicate) where TEntity : class
    {
        set.Where(predicate).AsTracking().Load();
        return set.Local.Where(predicate.Compile()).ToArray();
    }
}
