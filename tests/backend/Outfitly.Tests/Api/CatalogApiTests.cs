using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Outfitly.Tests.Api;

public sealed class CatalogApiTests
{
    [Fact]
    public async Task Catalogs_filter_count_and_page_only_their_own_visible_items()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        await factory.SeedOwnerAsync(other);
        using var client = await factory.OwnerClientAsync(owner);
        using var stranger = await factory.OwnerClientAsync(other);
        using var anonymous = factory.CreateClient();
        foreach (var name in new[] { "Shirt C", "Shirt A", "Shirt B" })
            (await client.PostAsJsonAsync("/api/items", new { name, category = "Top", color = "White" })).EnsureSuccessStatusCode();
        await client.PostAsJsonAsync("/api/items", new { name = "Shirt boots", category = "Footwear", color = "White" });
        await client.PostAsJsonAsync("/api/items", new { name = "Shirt blue", category = "Top", color = "Blue" });
        var foreign = await (await stranger.PostAsJsonAsync("/api/items", new { name = "Foreign shirt", category = "Top", color = "White" })).Content.ReadFromJsonAsync<JsonElement>();
        await stranger.PatchAsJsonAsync($"/api/items/{foreign.GetProperty("id").GetGuid()}/publication", new { isPublic = true });
        var page = await client.GetFromJsonAsync<JsonElement>("/api/items?search=SHIRT&category=top&color=white&page=2&pageSize=2");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal("Shirt C", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var descending = await client.GetFromJsonAsync<JsonElement>("/api/items?search=shirt&category=Top&color=white&sort=nameDesc&pageSize=2");
        Assert.Equal(new[] { "Shirt C", "Shirt B" }, descending.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        var publicPage = await anonymous.GetFromJsonAsync<JsonElement>("/api/wardrobe?search=shirt&category=Top&color=white");
        Assert.Equal(1, publicPage.GetProperty("total").GetInt32());
        Assert.Equal("Foreign shirt", Assert.Single(publicPage.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var empty = await client.GetFromJsonAsync<JsonElement>("/api/items?page=100");
        Assert.Equal(5, empty.GetProperty("total").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("sort=createdAt")]
    [InlineData("category=1")]
    [InlineData("category=Top,Bottom")]
    [InlineData("page=1&page=2")]
    public async Task Invalid_catalog_parameters_return_problem_details(string query)
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        foreach (var route in new[] { "/api/items", "/api/wardrobe" })
        {
            var response = await client.GetAsync($"{route}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }
}
