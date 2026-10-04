namespace Outfitly.Domain;

public sealed record WardrobeItem(
    Guid Id,
    string Name,
    ClothingCategory Category,
    string Color);

public enum ClothingCategory
{
    Top,
    Bottom,
    Dress,
    Outerwear,
    Footwear,
    Accessory
}
