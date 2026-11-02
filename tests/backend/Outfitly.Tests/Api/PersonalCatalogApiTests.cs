using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace Outfitly.Tests.Api;
public sealed class PersonalCatalogApiTests
{
    [Theory]
    [InlineData("brand=x&brand=y")]
    [InlineData("size=m&size=l")]
    [InlineData("publication=invalid")]
    [InlineData("publication=public&publication=private")]
    [InlineData("colorMissing=invalid")]
    [InlineData("brandMissing=true&brand=x")]
    [InlineData("colorMissing=true&color=")]
    [InlineData("sizeMissing=true&size=m")]
    [InlineData("sizeMissing=true&sizeMissing=false")]
    public async Task Invalid_filters_return_400(string filter)
    {
        using var factory = new ApiFactory(); var owner = Guid.NewGuid(); await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/items?{filter}")).StatusCode);
    }
    [Fact]
    public async Task Options_and_filtered_items_are_private()
    {
        using var factory = new ApiFactory(); var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner); await factory.SeedOwnerAsync(other);
        using var client = await factory.OwnerClientAsync(owner); using var stranger = await factory.OwnerClientAsync(other);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/items/filter-options")).StatusCode);
        (await stranger.PostAsJsonAsync("/api/items", new { name = "Foreign", category = "Top", brand = "Foreign" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top", brand = " BRAND ", description = "Cotton", size = "M" })).EnsureSuccessStatusCode();
        var options = await client.GetFromJsonAsync<JsonElement>($"/api/items/filter-options?ownerId={other}");
        Assert.Equal("brand", Assert.Single(options.GetProperty("brands").EnumerateArray()).GetString());
        var page = await client.GetFromJsonAsync<JsonElement>("/api/items?search=cotton&brand=brand&size=m&colorMissing=true&publication=private");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal("Shirt", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
    }
}
