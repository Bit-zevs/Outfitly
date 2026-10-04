using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure;

// Dependency-free executable regression suite. Nonzero exit code signals failure.
var tests = new (string Name, Action Run)[]
{
    ("Entities validate identifiers and required names", () =>
    {
        Throws<ArgumentException>(() => new User(Guid.Empty, "User"));
        Throws<ArgumentException>(() => new User(Guid.NewGuid(), " "));
        Throws<ArgumentException>(() => NewItem(Guid.Empty));
        Throws<ArgumentException>(() => new Outfit(Guid.NewGuid(), Guid.NewGuid(), ""));
        Throws<ArgumentException>(() => new ShareLink(Guid.Empty, ShareTargetType.Outfit, Guid.NewGuid()));
        Throws<ArgumentException>(() => new ShareLink(Guid.NewGuid(), ShareTargetType.Outfit, Guid.Empty));
        var user = new User(Guid.NewGuid(), " Alice ");
        user.Rename(" Bob ");
        Check(user.DisplayName == "Bob");
    }),
    ("Items are private and optional characteristics can be cleared", () =>
    {
        var item = NewItem(Guid.NewGuid());
        Check(!item.IsPublic && item.Color is null && item.PhotoUrl is null);
        item.Update(" Shirt ", ClothingCategory.Top, " White ", "M", "Brand", "Description", "https://example.com/photo");
        Check(item.Name == "Shirt" && item.Color == "White" && item.Size == "M");
        item.Update("Shirt", ClothingCategory.Top);
        Check(item.Size is null && item.Brand is null && item.Description is null && item.PhotoUrl is null);
        Throws<ArgumentOutOfRangeException>(() => item.Update("Changed", (ClothingCategory)99));
        Throws<ArgumentException>(() => item.Update("Changed", ClothingCategory.Top, photoUrl: "javascript:alert(1)"));
        Check(item.Name == "Shirt");
        item.Publish();
        Check(item.IsPublic);
        item.Unpublish();
        Check(!item.IsPublic);
    }),
    ("Outfits enforce ownership, uniqueness and nonempty publication", () =>
    {
        var owner = Guid.NewGuid();
        var outfit = new Outfit(Guid.NewGuid(), owner, " Outfit ");
        Check(!outfit.IsPublic);
        Throws<InvalidOperationException>(outfit.Publish);
        var item = NewItem(owner);
        outfit.AddItem(item);
        Throws<InvalidOperationException>(() => outfit.AddItem(item));
        Throws<InvalidOperationException>(() => outfit.AddItem(NewItem(Guid.NewGuid())));
        outfit.Update("New name", " Notes ");
        Check(outfit.Name == "New name" && outfit.Description == "Notes");
        outfit.Publish();
        outfit.RemoveItem(item.Id);
        Check(!outfit.IsPublic && outfit.ItemIds.Count == 0);
        Check(outfit.ItemIds is not List<Guid>);
    }),
    ("Share tokens are opaque and independently revocable", () =>
    {
        var id = Guid.NewGuid();
        var first = new ShareLink(Guid.NewGuid(), ShareTargetType.WardrobeItem, id);
        var second = new ShareLink(Guid.NewGuid(), ShareTargetType.WardrobeItem, id);
        Check(first.Token.Length == 64 && first.Token != second.Token && first.IsActive);
        Throws<ArgumentOutOfRangeException>(() => new ShareLink(Guid.NewGuid(), (ShareTargetType)99, id));
        first.Deactivate();
        first.Deactivate();
        Check(!first.IsActive && second.IsActive);
    }),
    ("Only the owner can edit, publish, share, revoke or delete", () =>
    {
        var (_, service, owner, item, outfit) = Setup();
        var other = Guid.NewGuid();
        var link = service.CreateShareLink(owner, ShareTargetType.WardrobeItem, item.Id);
        Throws<UnauthorizedAccessException>(() => service.UpdateItem(other, item.Id, "Other", ClothingCategory.Top));
        Throws<UnauthorizedAccessException>(() => service.UpdateOutfit(other, outfit.Id, "Other"));
        Throws<UnauthorizedAccessException>(() => service.SetPublication(other, ShareTargetType.WardrobeItem, item.Id, true));
        Throws<UnauthorizedAccessException>(() => service.SetPublication(other, ShareTargetType.Outfit, outfit.Id, true));
        Throws<UnauthorizedAccessException>(() => service.CreateShareLink(other, ShareTargetType.WardrobeItem, item.Id));
        Throws<UnauthorizedAccessException>(() => service.DeactivateShareLink(other, link.Id));
        Throws<UnauthorizedAccessException>(() => service.AddItemToOutfit(other, outfit.Id, item.Id));
        Throws<UnauthorizedAccessException>(() => service.RemoveItemFromOutfit(other, outfit.Id, item.Id));
        Throws<UnauthorizedAccessException>(() => service.DeleteItem(other, item.Id));
        Throws<UnauthorizedAccessException>(() => service.DeleteOutfit(other, outfit.Id));
        Check(item.Name == "Shirt" && link.IsActive);
    }),
    ("Application rejects missing and foreign items", () =>
    {
        var (_, service, owner, item, outfit) = Setup();
        Throws<KeyNotFoundException>(() => service.AddItemToOutfit(owner, outfit.Id, Guid.NewGuid()));
        var foreign = service.CreateItem(Guid.NewGuid(), "Foreign", ClothingCategory.Top);
        Throws<UnauthorizedAccessException>(() => service.AddItemToOutfit(owner, outfit.Id, foreign.Id));
        Throws<InvalidOperationException>(() => service.AddItemToOutfit(owner, outfit.Id, item.Id));
        Throws<ArgumentOutOfRangeException>(() => service.CreateShareLink(owner, (ShareTargetType)99, item.Id));
        Throws<KeyNotFoundException>(() => service.CreateShareLink(owner, ShareTargetType.Outfit, Guid.NewGuid()));
    }),
    ("Empty drafts cannot be published or shared", () =>
    {
        var service = new WardrobeService(new InMemoryWardrobeRepository());
        var owner = Guid.NewGuid();
        var outfit = service.CreateOutfit(owner, "Draft");
        Throws<InvalidOperationException>(() => service.SetPublication(owner, ShareTargetType.Outfit, outfit.Id, true));
        Throws<InvalidOperationException>(() => service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id));
    }),
    ("Item publication and all links are independent", () =>
    {
        var (_, service, owner, item, _) = Setup();
        var first = service.CreateShareLink(owner, ShareTargetType.WardrobeItem, item.Id);
        var second = service.CreateShareLink(owner, ShareTargetType.WardrobeItem, item.Id);
        Check(service.GetPublicItems().Count == 0 && service.GetSharedItem(first.Token) is not null);
        service.SetPublication(owner, ShareTargetType.WardrobeItem, item.Id, true);
        service.DeactivateShareLink(owner, first.Id);
        Check(service.GetPublicItems().Count == 1 && service.GetSharedItem(first.Token) is null);
        service.SetPublication(owner, ShareTargetType.WardrobeItem, item.Id, false);
        Check(service.GetPublicItems().Count == 0 && service.GetSharedItem(second.Token) is not null);
        Check(service.GetSharedItem("unknown") is null && service.GetSharedOutfit(second.Token) is null);
    }),
    ("Public and shared outfits include private items without publishing them", () =>
    {
        var (_, service, owner, item, outfit) = Setup();
        var link = service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id);
        Check(service.GetSharedOutfit(link.Token)?.Items.Single().Id == item.Id);
        Check(service.GetSharedItem(link.Token) is null);
        service.SetPublication(owner, ShareTargetType.Outfit, outfit.Id, true);
        Check(service.GetPublicOutfits().Single().Items.Single().Id == item.Id);
        Check(!item.IsPublic && service.GetPublicItems().Count == 0);
        service.SetPublication(owner, ShareTargetType.Outfit, outfit.Id, false);
        Check(service.GetSharedOutfit(link.Token) is not null);
        service.SetPublication(owner, ShareTargetType.Outfit, outfit.Id, true);
        service.DeactivateShareLink(owner, link.Id);
        Check(service.GetSharedOutfit(link.Token) is null && service.GetPublicOutfits().Count == 1);
    }),
    ("Deleting an item updates every outfit and revokes only affected links", () =>
    {
        var (repository, service, owner, item, emptyAfterDelete) = Setup();
        var stillPopulated = service.CreateOutfit(owner, "Another");
        var remaining = service.CreateItem(owner, "Shoes", ClothingCategory.Footwear);
        service.AddItemToOutfit(owner, stillPopulated.Id, item.Id);
        service.AddItemToOutfit(owner, stillPopulated.Id, remaining.Id);
        var itemLink = service.CreateShareLink(owner, ShareTargetType.WardrobeItem, item.Id);
        var itemLink2 = service.CreateShareLink(owner, ShareTargetType.WardrobeItem, item.Id);
        var emptyLink = service.CreateShareLink(owner, ShareTargetType.Outfit, emptyAfterDelete.Id);
        var emptyLink2 = service.CreateShareLink(owner, ShareTargetType.Outfit, emptyAfterDelete.Id);
        var remainingLink = service.CreateShareLink(owner, ShareTargetType.Outfit, stillPopulated.Id);
        service.SetPublication(owner, ShareTargetType.Outfit, emptyAfterDelete.Id, true);
        service.SetPublication(owner, ShareTargetType.Outfit, stillPopulated.Id, true);
        service.DeleteItem(owner, item.Id);
        Check(repository.GetAll().All(existing => existing.Id != item.Id));
        Check(!itemLink.IsActive && !itemLink2.IsActive && !emptyLink.IsActive && !emptyLink2.IsActive);
        Check(!emptyAfterDelete.IsPublic && emptyAfterDelete.ItemIds.Count == 0);
        Check(stillPopulated.IsPublic && stillPopulated.ItemIds.Single() == remaining.Id && remainingLink.IsActive);
        Check(service.GetSharedOutfit(remainingLink.Token)?.Items.Single().Id == remaining.Id);
        Check(service.GetSharedItem(itemLink.Token) is null && service.GetSharedOutfit(emptyLink.Token) is null);
    }),
    ("Removing the last outfit item revokes links but does not delete the item", () =>
    {
        var (repository, service, owner, item, outfit) = Setup();
        var link = service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id);
        service.SetPublication(owner, ShareTargetType.Outfit, outfit.Id, true);
        service.RemoveItemFromOutfit(owner, outfit.Id, item.Id);
        Check(!link.IsActive && !outfit.IsPublic && repository.GetAll().Count == 1);
        service.AddItemToOutfit(owner, outfit.Id, item.Id);
        Check(service.GetSharedOutfit(link.Token) is null);
    }),
    ("Deleting an outfit revokes every link and preserves its items", () =>
    {
        var (repository, service, owner, item, outfit) = Setup();
        var first = service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id);
        var second = service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id);
        service.DeleteOutfit(owner, outfit.Id);
        Check(!first.IsActive && !second.IsActive && repository.GetOutfits().Count == 0);
        Check(repository.GetAll().Single().Id == item.Id);
        Check(service.GetSharedOutfit(first.Token) is null);
    })
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} scenarios passed.");
return failures == 0 ? 0 : 1;

static WardrobeItem NewItem(Guid owner) =>
    new(Guid.NewGuid(), owner, "Shirt", ClothingCategory.Top);

static (InMemoryWardrobeRepository Repository, WardrobeService Service, Guid Owner,
    WardrobeItem Item, Outfit Outfit) Setup()
{
    var repository = new InMemoryWardrobeRepository();
    var service = new WardrobeService(repository);
    var owner = Guid.NewGuid();
    var item = service.CreateItem(owner, "Shirt", ClothingCategory.Top);
    var outfit = service.CreateOutfit(owner, "Daily");
    service.AddItemToOutfit(owner, outfit.Id, item.Id);
    return (repository, service, owner, item, outfit);
}

static void Check(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Assertion failed.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
