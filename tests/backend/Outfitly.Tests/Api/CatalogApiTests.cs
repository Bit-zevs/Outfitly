using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Outfitly.Tests.Api;

public sealed class CatalogApiTests
{
    [Fact]
    public async Task Personal_outfit_pages_include_private_drafts_but_never_other_owners()
    {
        using var factory = new ApiFactory(); var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner); await factory.SeedOwnerAsync(other);
        using var client = await factory.OwnerClientAsync(owner); using var stranger = await factory.OwnerClientAsync(other);
        foreach (var name in new[] { "Look C", "Look A", "Look B" })
            (await client.PostAsJsonAsync("/api/outfits", new { name })).EnsureSuccessStatusCode();
        (await stranger.PostAsJsonAsync("/api/outfits", new { name = "Look foreign" })).EnsureSuccessStatusCode();
        var page = await client.GetFromJsonAsync<JsonElement>("/api/outfits?search=LOOK&sort=nameDesc&page=2&pageSize=2");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal("Look A", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var empty = await stranger.GetFromJsonAsync<JsonElement>("/api/outfits?search=absent");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/outfits?pageSize=101")).StatusCode);
    }
    [Fact]
    public async Task Public_outfits_search_sort_page_and_reject_private_direct_reads()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid(); await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner); using var guest = factory.CreateClient();
        var item = await (await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" })).Content.ReadFromJsonAsync<JsonElement>();
        Guid privateId = default, publicId = default, likedId = default;
        foreach (var name in new[] { "Walk C", "Walk A", "Walk B", "Walk private", "Other" })
        {
            var created = await (await client.PostAsJsonAsync("/api/outfits", new { name })).Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("id").GetGuid();
            if (name == "Walk C") likedId = id;
            (await client.PostAsync($"/api/outfits/{id}/items/{item.GetProperty("id").GetGuid()}", null)).EnsureSuccessStatusCode();
            if (name == "Walk private") privateId = id;
            else { (await client.PatchAsJsonAsync($"/api/outfits/{id}/publication", new { isPublic = true })).EnsureSuccessStatusCode(); publicId = id; }
        }
        var page = await guest.GetFromJsonAsync<JsonElement>("/api/wardrobe/outfits?search=WALK&sort=nameDesc&pageSize=2&page=2");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal("Walk A", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var first = await guest.GetFromJsonAsync<JsonElement>("/api/wardrobe/outfits?search=walk&sort=nameDesc&pageSize=2");
        Assert.Equal(new[] { "Walk C", "Walk B" }, first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/wardrobe/outfits/{privateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/wardrobe/outfits/{publicId}")).StatusCode);
        var readerId = Guid.NewGuid();
        await factory.SeedOwnerAsync(readerId);
        using var reader = await factory.OwnerClientAsync(readerId);
        (await reader.PutAsync($"/api/outfits/{likedId}/like", null)).EnsureSuccessStatusCode();
        (await reader.PostAsync($"/api/outfits/{publicId}/views", null)).EnsureSuccessStatusCode();
        foreach (var (sort, expected) in new[] { ("", publicId), ("&sort=likes", likedId), ("&sort=views", publicId) })
        {
            var sorted = await guest.GetFromJsonAsync<JsonElement>($"/api/wardrobe/outfits?pageSize=1{sort}");
            Assert.Equal(expected, Assert.Single(sorted.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }
        foreach (var invalid in new[] { "page=0", "pageSize=101", "sort=bad", "page=1&page=2" })
            Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/wardrobe/outfits?" + invalid)).StatusCode);
    }
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
    [InlineData("/api/wardrobe")]
    [InlineData("/api/wardrobe/outfits")]
    public async Task Public_catalogs_reject_conflicting_attribute_filters(string route)
    {
        using var factory = new ApiFactory();
        using var guest = factory.CreateClient();
        using var response = await guest.GetAsync($"{route}?brand=brand&brandMissing=true");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Public_options_respect_visibility_and_outfit_filters_match_one_piece()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        using var guest = factory.CreateClient();

        async Task<Guid> Item(string name, string color, string brand, string size, bool publish = false)
        {
            using var created = await client.PostAsJsonAsync("/api/items", new { name, category = "Top", color, brand, size, description = "Made with linen" });
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            if (publish)
                (await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
            return id;
        }

        async Task<Guid> Outfit(string name, bool publish, params Guid[] items)
        {
            using var created = await client.PostAsJsonAsync("/api/outfits", new { name, description = "Evening walk" });
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            foreach (var itemId in items)
                (await client.PostAsync($"/api/outfits/{id}/items/{itemId}", null)).EnsureSuccessStatusCode();
            if (publish)
                (await client.PatchAsJsonAsync($"/api/outfits/{id}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
            return id;
        }

        var publicItem = await Item("Coat", "White", "Studio", "M", publish: true);
        var red = await Item("Sweater", "Red", "Atelier", "S");
        var blue = await Item("Shirt", "Blue", "Atelier", "M");
        var matching = await Item("Blouse", "Red", "Atelier", "M");
        var hidden = await Item("Private", "Hidden", "Secret", "M");
        await Outfit("Split attributes", true, red, blue);
        var publicOutfit = await Outfit("Matching attributes", true, matching);
        await Outfit("Private outfit", false, hidden, matching);

        var itemOptions = await guest.GetFromJsonAsync<JsonElement>("/api/wardrobe/filter-options");
        Assert.Equal("studio", Assert.Single(itemOptions.GetProperty("brands").EnumerateArray()).GetString());
        var outfitOptions = await guest.GetFromJsonAsync<JsonElement>("/api/wardrobe/outfits/filter-options");
        Assert.Equal(new[] { "blue", "red" }, outfitOptions.GetProperty("colors").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("atelier", Assert.Single(outfitOptions.GetProperty("brands").EnumerateArray()).GetString());

        var items = await guest.GetFromJsonAsync<JsonElement>("/api/wardrobe?search=LINEN&brand=STUDIO&size=m&category=Top");
        Assert.Equal(1, items.GetProperty("total").GetInt32());
        Assert.Equal(publicItem, Assert.Single(items.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var privateItems = await guest.GetFromJsonAsync<JsonElement>("/api/wardrobe?brand=Atelier");
        Assert.Equal(0, privateItems.GetProperty("total").GetInt32());
        var outfits = await guest.GetFromJsonAsync<JsonElement>("/api/wardrobe/outfits?search=evening&category=Top&color=RED&brand=atelier&size=m");
        Assert.Equal(1, outfits.GetProperty("total").GetInt32());
        Assert.Equal(publicOutfit, Assert.Single(outfits.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
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
