using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Infrastructure;

public sealed class InMemoryWardrobeRepository : IWardrobeRepository
{
    private readonly List<WardrobeItem> _items = [];
    private readonly List<Outfit> _outfits = [];
    private readonly List<ShareLink> _links = [];

    public IReadOnlyCollection<WardrobeItem> GetAll() => _items.ToArray();
    public IReadOnlyCollection<Outfit> GetOutfits() => _outfits.ToArray();
    public IReadOnlyCollection<ShareLink> GetLinks() => _links.ToArray();

    // This single-process test adapter has no independent persistence snapshots.
    public void MarkOutfitChanged(Outfit outfit) => ArgumentNullException.ThrowIfNull(outfit);

    public void Add(WardrobeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_items.Any(existing => existing.Id == item.Id))
            throw new InvalidOperationException("An item with this identifier already exists.");
        _items.Add(item);
    }

    public void Add(Outfit outfit)
    {
        ArgumentNullException.ThrowIfNull(outfit);
        if (_outfits.Any(existing => existing.Id == outfit.Id))
            throw new InvalidOperationException("An outfit with this identifier already exists.");
        _outfits.Add(outfit);
    }

    public void Add(ShareLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (_links.Any(existing => existing.Id == link.Id || existing.Token == link.Token))
            throw new InvalidOperationException("A share link with this identifier or token already exists.");
        _links.Add(link);
    }

    public void Remove(WardrobeItem item) => _items.Remove(item);
    public void Remove(Outfit outfit) => _outfits.Remove(outfit);
}
