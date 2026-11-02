using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace Outfitly.Tests.Api;
public sealed class StatisticsApiTests
{
    [Fact]
    public async Task Statistics_require_identity_and_ignore_client_owner()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner); await factory.SeedOwnerAsync(other);
        using var client = await factory.OwnerClientAsync(owner);
        using var stranger = await factory.OwnerClientAsync(other);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/wardrobe/statistics")).StatusCode);
        (await stranger.PostAsJsonAsync("/api/items", new { name = "Foreign", category = "Top" })).EnsureSuccessStatusCode();
        var result = await client.GetFromJsonAsync<JsonElement>($"/api/wardrobe/statistics?ownerId={other}");
        Assert.Equal(0, result.GetProperty("items").GetInt32()); Assert.Equal(6, result.GetProperty("categories").GetArrayLength());
    }
    [Fact]
    public async Task Reactions_survive_unpublishing_and_links_but_not_resource_deletion()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid(); var viewer = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner); await factory.SeedOwnerAsync(viewer);
        using var client = await factory.OwnerClientAsync(owner);
        using var visitor = await factory.OwnerClientAsync(viewer);
        var created = await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        for (var i = 0; i < 2; i++)
            (await visitor.PostAsync($"/api/items/{id}/views", null)).EnsureSuccessStatusCode();
        (await visitor.PutAsync($"/api/items/{id}/like", null)).EnsureSuccessStatusCode();
        (await visitor.DeleteAsync($"/api/items/{id}/like")).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = false })).EnsureSuccessStatusCode();
        var linkResponse = await client.PostAsync($"/api/items/{id}/share-links", null);
        linkResponse.EnsureSuccessStatusCode();
        var token = (await linkResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        (await visitor.PutAsync($"/api/items/{id}/like?shareToken={token}", null)).EnsureSuccessStatusCode();
        var stats = await client.GetFromJsonAsync<JsonElement>("/api/wardrobe/statistics");
        Assert.Equal(0, stats.GetProperty("publicItems").GetInt32());
        Assert.Equal(1, stats.GetProperty("itemReactions").GetProperty("views").GetInt32());
        Assert.Equal(1, stats.GetProperty("itemReactions").GetProperty("likes").GetInt32());
        (await client.DeleteAsync($"/api/items/{id}")).EnsureSuccessStatusCode();
        stats = await client.GetFromJsonAsync<JsonElement>("/api/wardrobe/statistics");
        Assert.Equal(0, stats.GetProperty("itemReactions").GetProperty("views").GetInt32());
        Assert.Equal(0, stats.GetProperty("itemReactions").GetProperty("likes").GetInt32());
    }}


