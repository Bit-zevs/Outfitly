namespace Outfitly.Domain;

public sealed class WardrobeItem
{
    public Guid Id { get; }
    public Guid OwnerId { get; }
    public string Name { get; private set; }
    public ClothingCategory Category { get; private set; }
    public string? Color { get; private set; }
    public string? Size { get; private set; }
    public string? Brand { get; private set; }
    public string? Description { get; private set; }
    public string? PhotoUrl { get; private set; }
    public bool IsPublic { get; private set; }

    public WardrobeItem(Guid id, Guid ownerId, string name, ClothingCategory category,
        string? color = null, string? size = null, string? brand = null,
        string? description = null, string? photoUrl = null)
    {
        Id = Guard.Id(id);
        OwnerId = Guard.Id(ownerId);
        Name = Guard.Name(name);
        Update(name, category, color, size, brand, description, photoUrl);
    }

    public void Update(string name, ClothingCategory category, string? color = null,
        string? size = null, string? brand = null, string? description = null, string? photoUrl = null)
    {
        var validName = Guard.Name(name);
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        var validPhotoUrl = Guard.PhotoUrl(photoUrl);
        Name = validName;
        Category = category;
        Color = Guard.Optional(color);
        Size = Guard.Optional(size);
        Brand = Guard.Optional(brand);
        Description = Guard.Optional(description);
        PhotoUrl = validPhotoUrl;
    }

    public void Publish() => IsPublic = true;
    public void Unpublish() => IsPublic = false;
}

public enum ClothingCategory
{
    Top,
    Bottom,
    Dress,
    Outerwear,
    Footwear,
    Accessory
}
