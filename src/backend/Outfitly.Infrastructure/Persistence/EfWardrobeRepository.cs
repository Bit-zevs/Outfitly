using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence;

/// <summary>
/// Tracks a unit of work. The caller commits the complete use case with DbContext.SaveChangesAsync.
/// </summary>
public sealed class EfWardrobeRepository(OutfitlyDbContext context) : IWardrobeRepository
{
    public IReadOnlyCollection<WardrobeItem> GetAll() => Read(context.WardrobeItems);
    public IReadOnlyCollection<Outfit> GetOutfits() => Read(context.Outfits);
    public IReadOnlyCollection<ShareLink> GetLinks() => Read(context.ShareLinks);

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

    private static IReadOnlyCollection<TEntity> Read<TEntity>(DbSet<TEntity> set) where TEntity : class
    {
        // Local includes pending additions and excludes pending deletions.
        // Tracking is required because the existing service mutates returned domain entities.
        set.AsTracking().Load();
        return set.Local.ToArray();
    }
}
