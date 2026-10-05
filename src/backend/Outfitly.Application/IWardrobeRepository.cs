using Outfitly.Domain;

namespace Outfitly.Application;

public interface IWardrobeRepository
{
    IReadOnlyCollection<WardrobeItem> GetAll();
    IReadOnlyCollection<Outfit> GetOutfits();
    IReadOnlyCollection<ShareLink> GetLinks();
    // Coordinate every command affecting an outfit, including its links, with
    // the version observed when the outfit was read. Conflicts fail the whole commit.
    void MarkOutfitChanged(Outfit outfit);
    void Add(WardrobeItem item);
    void Add(Outfit outfit);
    void Add(ShareLink link);
    void Remove(WardrobeItem item);
    void Remove(Outfit outfit);
}
