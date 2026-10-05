using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Outfitly.Api.Contracts;

namespace Outfitly.Tests.Api;

public sealed class WardrobeApiTests
{
    [Fact]
    public async Task Removing_the_last_member_revokes_links_and_refilling_does_not_reactivate_them()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);
        using var anonymous = factory.CreateClient();
        var itemId = (await Json(await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" })))
            .GetProperty("id").GetGuid();
        var outfitId = (await Json(await client.PostAsJsonAsync("/api/outfits", new { name = "Daily" })))
            .GetProperty("id").GetGuid();
        var route = $"/api/outfits/{outfitId}";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{route}/items/{itemId}", null)).StatusCode);
        var link = await (await client.PostAsync($"{route}/share-links", null)).Content.ReadFromJsonAsync<ShareLinkResponse>();
        Assert.NotNull(link);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"{route}/publication", new { isPublic = true })).StatusCode);

        var removed = await client.DeleteAsync($"{route}/items/{itemId}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(link.Url)).StatusCode);
        Assert.Empty((await Json(await client.GetAsync(route))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{route}/items/{itemId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(link.Url)).StatusCode);
        Assert.False((await Json(await client.GetAsync(route))).GetProperty("isPublic").GetBoolean());
        Assert.Single((await Json(await client.GetAsync("/api/outfits"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/items/{itemId}")).StatusCode);
    }

    [Fact]
    public async Task An_omitted_publication_flag_is_not_interpreted_as_unpublishing()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);
        var id = (await Json(await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" })))
            .GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true })).StatusCode);

        var response = await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(await client.GetAsync($"/api/items/{id}"))).GetProperty("isPublic").GetBoolean());
    }

    [Fact]
    public async Task Production_authentication_does_not_trust_client_supplied_user_headers()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invented-token");

        var response = await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/items")]
    [InlineData("GET", "/api/outfits")]
    [InlineData("POST", "/api/outfits")]
    [InlineData("DELETE", "/api/share-links/11111111-1111-1111-1111-111111111111")]
    public async Task Private_routes_require_an_authenticated_identity(string method, string route)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task An_authenticated_identity_without_a_valid_user_id_is_forbidden(string actor)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", actor);

        var response = await client.GetAsync("/api/items");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Item_crud_persists_changes_and_ignores_an_owner_supplied_in_the_body()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);

        var created = await client.PostAsJsonAsync("/api/items", new
        {
            name = " Shirt ", category = "Top", color = "White", ownerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await Json(created);
        var id = item.GetProperty("id").GetGuid();
        Assert.Equal(owner, item.GetProperty("ownerId").GetGuid());
        Assert.Equal("Shirt", item.GetProperty("name").GetString());
        Assert.Equal($"/api/items/{id}", created.Headers.Location?.OriginalString);
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new { name = "Shoes", category = "Footwear" });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var reloaded = await Json(await client.GetAsync($"/api/items/{id}"));
        Assert.Equal("Shoes", reloaded.GetProperty("name").GetString());
        Assert.Equal("Footwear", reloaded.GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, reloaded.GetProperty("color").ValueKind);
        Assert.Single((await Json(await client.GetAsync("/api/items"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/items/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/items/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"Shirt\"}")]
    [InlineData("{\"name\":\"Shirt\",\"category\":0}")]
    [InlineData("{\"name\":\"Shirt\",\"category\":\"Unknown\"}")]
    [InlineData("{\"name\":\" \",\"category\":\"Top\"}")]
    [InlineData("{\"name\":\"Shirt\",\"category\":\"Top\",\"photoUrl\":\"javascript:alert(1)\"}")]
    public async Task Invalid_requests_return_400_and_do_not_create_an_item(string json)
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);

        var response = await client.PostAsync("/api/items", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty((await Json(await client.GetAsync("/api/items"))).EnumerateArray());
    }

    [Fact]
    public async Task Another_owner_cannot_read_or_modify_a_private_item()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        await factory.SeedOwnerAsync(other);
        using var client = factory.OwnerClient(owner);
        var item = await Json(await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" }));
        var id = item.GetProperty("id").GetGuid();
        using var stranger = factory.OwnerClient(other);

        var update = await stranger.PutAsJsonAsync($"/api/items/{id}", new { name = "Stolen", category = "Bottom" });

        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/items/{id}")).StatusCode);
        Assert.Empty((await Json(await stranger.GetAsync("/api/items"))).EnumerateArray());
        Assert.Equal("Shirt", (await Json(await client.GetAsync($"/api/items/{id}"))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Outfit_sharing_and_item_deletion_work_across_independent_HTTP_requests()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);
        using var anonymous = factory.CreateClient();
        var item = await Json(await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" }));
        var itemId = item.GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync("/api/outfits", new { name = "Daily", description = "Notes" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var outfitId = (await Json(created)).GetProperty("id").GetGuid();
        var route = $"/api/outfits/{outfitId}";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(route, new { name = "Updated outfit" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"{route}/share-links", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"{route}/publication", new { isPublic = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{route}/items/{itemId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"{route}/items/{itemId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"{route}/publication", new { isPublic = true })).StatusCode);
        var linkResponse = await client.PostAsync($"{route}/share-links", null);
        var link = await linkResponse.Content.ReadFromJsonAsync<ShareLinkResponse>();
        Assert.Equal(HttpStatusCode.Created, linkResponse.StatusCode);
        Assert.NotNull(link);
        Assert.Equal(link.Url, linkResponse.Headers.Location?.OriginalString);
        var shared = await Json(await anonymous.GetAsync(link.Url));
        Assert.Equal("Updated outfit", shared.GetProperty("name").GetString());
        Assert.Equal(itemId, Assert.Single(shared.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty((await Json(await anonymous.GetAsync("/api/wardrobe"))).EnumerateArray());
        Assert.Single((await Json(await anonymous.GetAsync("/api/wardrobe/outfits"))).EnumerateArray());
        Assert.DoesNotContain("token", shared.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var deleted = await client.DeleteAsync($"/api/items/{itemId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(link.Url)).StatusCode);
        Assert.Empty((await Json(await anonymous.GetAsync("/api/wardrobe/outfits"))).EnumerateArray());
        Assert.Empty((await Json(await client.GetAsync(route))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task Revoking_an_item_link_preserves_publication_and_other_links()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);
        using var anonymous = factory.CreateClient();
        var id = (await Json(await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" })))
            .GetProperty("id").GetGuid();
        await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true });
        var first = await (await client.PostAsync($"/api/items/{id}/share-links", null)).Content.ReadFromJsonAsync<ShareLinkResponse>();
        var second = await (await client.PostAsync($"/api/items/{id}/share-links", null)).Content.ReadFromJsonAsync<ShareLinkResponse>();
        Assert.NotNull(first);
        Assert.NotNull(second);

        var response = await client.DeleteAsync($"/api/share-links/{first.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(second.Url)).StatusCode);
        Assert.Single((await Json(await anonymous.GetAsync("/api/wardrobe"))).EnumerateArray());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
