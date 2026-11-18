using Outfitly.Domain;

namespace Outfitly.Application;

public interface IWardrobeRepository
{
    IReadOnlyCollection<WardrobeItem> GetAll();
    IReadOnlyCollection<Outfit> GetOutfits();
    IReadOnlyCollection<ShareLink> GetLinks();
    // Bounded, read-only queries for anonymous share access.
    ShareLink? FindActiveLink(string token, ShareTargetType targetType);
    WardrobeItem? ReadItem(Guid id);
    Outfit? ReadOutfit(Guid id);
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
