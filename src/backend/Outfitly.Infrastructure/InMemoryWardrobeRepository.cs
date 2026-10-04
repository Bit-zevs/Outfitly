using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Infrastructure;

public sealed class InMemoryWardrobeRepository : IWardrobeRepository
{
    private readonly List<WardrobeItem> _items = [];

    public IReadOnlyCollection<WardrobeItem> GetAll() => _items.AsReadOnly();
}
