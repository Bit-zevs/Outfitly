namespace Outfitly.Domain;

public sealed class Outfit
{
    private readonly List<WardrobeItem> _items = [];

    public Guid Id { get; }
    public Guid OwnerId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsPublic { get; private set; }
    public IReadOnlyCollection<Guid> ItemIds => _items.Select(item => item.Id).ToArray();

    public Outfit(Guid id, Guid ownerId, string name, string? description = null)
    {
        Id = Guard.Id(id);
        OwnerId = Guard.Id(ownerId);
        Name = Guard.Name(name);
        Description = Guard.Optional(description);
    }

    public void Update(string name, string? description = null)
    {
        Name = Guard.Name(name);
        Description = Guard.Optional(description);
    }

    public void AddItem(WardrobeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.OwnerId != OwnerId)
            throw new InvalidOperationException("An outfit can contain only its owner's items.");
        if (_items.Any(existing => existing.Id == item.Id))
            throw new InvalidOperationException("The item is already in this outfit.");
        _items.Add(item);
    }

    public void RemoveItem(Guid itemId)
    {
        var id = Guard.Id(itemId);
        _items.RemoveAll(item => item.Id == id);
        if (_items.Count == 0)
            Unpublish();
    }

    public void EnsureCanShare()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("An empty outfit cannot be published or shared.");
    }

    public void Publish()
    {
        EnsureCanShare();
        IsPublic = true;
    }

    public void Unpublish() => IsPublic = false;
}
