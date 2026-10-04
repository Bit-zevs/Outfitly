using Outfitly.Domain;

namespace Outfitly.Application;

public interface IWardrobeRepository
{
    IReadOnlyCollection<WardrobeItem> GetAll();
}
