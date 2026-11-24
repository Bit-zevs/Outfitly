using Outfitly.Domain;

namespace Outfitly.Application;

public interface IWardrobeRepository
{
    WardrobeItem? FindItemForChange(Guid id);
    ShareLink? FindLinkForChange(Guid id);
    IReadOnlyCollection<ShareLink> FindTargetLinks(ShareTargetType type, Guid id);
    IReadOnlyCollection<Outfit> FindOutfitsContaining(Guid ownerId, Guid itemId);
    IReadOnlyCollection<WardrobeItem> FindItemsForChange(IReadOnlyCollection<Guid> ids);
    IReadOnlyCollection<WardrobeItem> FindItems(Guid? ownerId = null, bool publicOnly = false);
    IReadOnlyCollection<Outfit> FindOutfits(Guid? ownerId = null, bool publicOnly = false);
    // Bounded, read-only queries for anonymous share access.
    ShareLink? FindActiveLink(string token, ShareTargetType targetType);
    WardrobeItem? ReadItem(Guid id, bool publicOnly = false);
    Outfit? ReadOutfit(Guid id, bool publicOnly = false);
    Outfit? FindOutfitForChange(Guid id);
    IReadOnlyCollection<WardrobeItem> ReadItems(IReadOnlyCollection<Guid> ids);
    // Coordinate every command affecting an outfit, including its links, with
    // the version observed when the outfit was read. Conflicts fail the whole commit.
    void MarkOutfitChanged(Outfit outfit);
    void Add(WardrobeItem item);
    void Add(Outfit outfit);
    void Add(ShareLink link);
    void Remove(WardrobeItem item);
    void Remove(Outfit outfit);
}
