using Outfitly.Domain;

namespace Outfitly.Tests.Domain;

public sealed class WardrobeItemTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void A_blank_name_cannot_replace_valid_characteristics(string? name)
    {
        var item = Create();

        Assert.ThrowsAny<ArgumentException>(() => item.Update(name!, ClothingCategory.Bottom));

        AssertOriginal(item);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void An_unknown_category_cannot_partially_update_an_item(int category)
    {
        var item = Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => item.Update("Changed", (ClothingCategory)category));

        AssertOriginal(item);
    }

    [Theory]
    [InlineData("/relative.jpg")]
    [InlineData("ftp://example.com/photo.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a URL")]
    public void An_invalid_photo_cannot_partially_update_an_item(string url)
    {
        var item = Create();

        Assert.Throws<ArgumentException>(() => item.Update("Changed", ClothingCategory.Bottom,
            "Black", "L", "Other", "New description", url));

        AssertOriginal(item);
    }

    [Theory]
    [InlineData("http://example.com/photo.jpg")]
    [InlineData("https://example.com/photo.jpg")]
    public void Absolute_web_photos_and_trimmed_characteristics_are_accepted(string url)
    {
        var item = Create();

        item.Update(" Shirt ", ClothingCategory.Accessory, " White ", " M ", " Brand ", " Notes ", $" {url} ");

        Assert.Equal("Shirt", item.Name);
        Assert.Equal(ClothingCategory.Accessory, item.Category);
        Assert.Equal("White", item.Color);
        Assert.Equal("M", item.Size);
        Assert.Equal("Brand", item.Brand);
        Assert.Equal("Notes", item.Description);
        Assert.Equal(url, item.PhotoUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Empty_optional_values_clear_previous_characteristics(string? empty)
    {
        var item = Create();

        item.Update("Shirt", ClothingCategory.Top, empty, empty, empty, empty, empty);

        Assert.Null(item.Color);
        Assert.Null(item.Size);
        Assert.Null(item.Brand);
        Assert.Null(item.Description);
        Assert.Null(item.PhotoUrl);
    }

    [Theory]
    [InlineData(ClothingCategory.Top)]
    [InlineData(ClothingCategory.Accessory)]
    public void The_first_and_last_categories_are_valid(ClothingCategory category)
    {
        var item = new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "Item", category);

        Assert.Equal(category, item.Category);
        Assert.False(item.IsPublic);
    }

    private static WardrobeItem Create() => new(Guid.NewGuid(), Guid.NewGuid(), "Original",
        ClothingCategory.Top, "Blue", "S", "Original brand", "Original notes", "https://example.com/original.jpg");

    private static void AssertOriginal(WardrobeItem item)
    {
        Assert.Equal("Original", item.Name);
        Assert.Equal(ClothingCategory.Top, item.Category);
        Assert.Equal("Blue", item.Color);
        Assert.Equal("S", item.Size);
        Assert.Equal("Original brand", item.Brand);
        Assert.Equal("Original notes", item.Description);
        Assert.Equal("https://example.com/original.jpg", item.PhotoUrl);
    }
}
