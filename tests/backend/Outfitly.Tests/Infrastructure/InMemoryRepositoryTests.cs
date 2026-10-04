using Outfitly.Domain;
using Outfitly.Infrastructure;

namespace Outfitly.Tests.Infrastructure;

public sealed class InMemoryRepositoryTests
{
    [Fact]
    public async Task An_in_flight_read_remains_valid_when_another_thread_adds_an_item()
    {
        var repository = new InMemoryWardrobeRepository();
        var owner = Guid.NewGuid();
        var shirt = new WardrobeItem(Guid.NewGuid(), owner, "Shirt", ClothingCategory.Top);
        var shoes = new WardrobeItem(Guid.NewGuid(), owner, "Shoes", ClothingCategory.Footwear);
        repository.Add(shirt);
        repository.Add(shoes);
        using var read = repository.GetAll().GetEnumerator();
        Assert.True(read.MoveNext());
        var seen = new List<Guid> { read.Current.Id };

        await Task.Run(() => repository.Add(new WardrobeItem(Guid.NewGuid(), owner, "Coat", ClothingCategory.Outerwear)));

        while (read.MoveNext()) seen.Add(read.Current.Id);
        Assert.Contains(shirt.Id, seen);
        Assert.Contains(shoes.Id, seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(3, repository.GetAll().Count);
    }

    [Fact]
    public void A_second_item_with_the_same_identity_does_not_replace_the_first()
    {
        var repository = new InMemoryWardrobeRepository();
        var original = new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "Original", ClothingCategory.Top);
        repository.Add(original);

        Assert.Throws<InvalidOperationException>(() => repository.Add(
            new WardrobeItem(original.Id, original.OwnerId, "Replacement", ClothingCategory.Bottom)));

        Assert.Equal("Original", Assert.Single(repository.GetAll()).Name);
    }
}
