using System.Net;
using System.Net.Http.Json;
using Outfitly.Application;
using Outfitly.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Outfitly.Tests.Api;

public sealed class CategoryApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    [Theory]
    [InlineData("Top, Bottom")]
    [InlineData("Bottom, Dress")]
    [InlineData("Top, Top")]
    [InlineData("1")]
    [InlineData(" Top ")]
    public async Task Invalid_category_strings_cannot_create_or_overwrite_an_item(string category)
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);

        var rejectedCreate = await client.PostAsJsonAsync("/api/items", new { name = "Invalid", category });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedCreate.StatusCode);
        Assert.Equal("application/problem+json", rejectedCreate.Content.Headers.ContentType?.MediaType);
        Assert.Empty((await client.GetFromJsonAsync<ItemView[]>("/api/items", Json))!);

        var created = await client.PostAsJsonAsync("/api/items", new { name = "Original", category = "Top", color = "White" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = (await created.Content.ReadFromJsonAsync<ItemView>(Json))!;
        var rejectedUpdate = await client.PutAsJsonAsync($"/api/items/{original.Id}", new { name = "Overwritten", category });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedUpdate.StatusCode);
        Assert.Equal("application/problem+json", rejectedUpdate.Content.Headers.ContentType?.MediaType);
        Assert.Equal(original, await client.GetFromJsonAsync<ItemView>($"/api/items/{original.Id}", Json));
    }

    [Theory]
    [InlineData("Top", ClothingCategory.Top)]
    [InlineData("bottom", ClothingCategory.Bottom)]
    [InlineData("Dress", ClothingCategory.Dress)]
    [InlineData("Outerwear", ClothingCategory.Outerwear)]
    [InlineData("Footwear", ClothingCategory.Footwear)]
    [InlineData("Accessory", ClothingCategory.Accessory)]
    public async Task Each_single_category_name_round_trips_without_changing_its_value(string name, ClothingCategory expected)
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);
        var created = await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = (await created.Content.ReadFromJsonAsync<ItemView>(Json))!;
        Assert.Equal(expected, item.Category);
        var update = await client.PutAsJsonAsync($"/api/items/{item.Id}", new { name = "Updated", category = name });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var saved = (await client.GetFromJsonAsync<ItemView>($"/api/items/{item.Id}", Json))!;
        Assert.Equal(expected, saved.Category);
        Assert.Equal("Updated", saved.Name);
    }
}
