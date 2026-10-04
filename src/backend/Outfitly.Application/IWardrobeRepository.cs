using Outfitly.Domain;

namespace Outfitly.Application;

public interface IWardrobeRepository
{
    IReadOnlyCollection<WardrobeItem> GetAll();
    IReadOnlyCollection<Outfit> GetOutfits();
    IReadOnlyCollection<ShareLink> GetLinks();
    void Add(WardrobeItem item);
    void Add(Outfit outfit);
    void Add(ShareLink link);
    void Remove(WardrobeItem item);
    void Remove(Outfit outfit);
}
