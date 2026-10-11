using System.Net;
using System.Net.Http.Json;
using Outfitly.Api.Contracts;

namespace Outfitly.Tests.Api;

public sealed class ShareLinkListApiTests
{
    [Theory]
    [InlineData("items")]
    [InlineData("outfits")]
    public async Task Lists_survive_new_requests_and_exclude_revoked_and_foreign_links(string resource)
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        await factory.SeedOwnerAsync(other);
        using var client = await factory.OwnerClientAsync(owner);
        var itemId = await PhotoApiTests.CreateItem(client);
        var id = itemId;
        if (resource == "outfits")
        {
            var created = await (await client.PostAsJsonAsync("/api/outfits", new { name = "Daily" })).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            id = created.GetProperty("id").GetGuid();
            (await client.PostAsync($"/api/outfits/{id}/items/{itemId}", null)).EnsureSuccessStatusCode();
        }
        var route = $"/api/{resource}/{id}/share-links";
        Assert.Empty((await client.GetFromJsonAsync<ShareLinkResponse[]>(route))!);
        var first = (await (await client.PostAsync(route, null)).Content.ReadFromJsonAsync<ShareLinkResponse>())!;
        var second = (await (await client.PostAsync(route, null)).Content.ReadFromJsonAsync<ShareLinkResponse>())!;
        using var reopened = await factory.OwnerClientAsync(owner);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), (await reopened.GetFromJsonAsync<ShareLinkResponse[]>(route))!.Select(link => link.Id).Order());
        (await reopened.DeleteAsync($"/api/share-links/{first.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(second, Assert.Single((await reopened.GetFromJsonAsync<ShareLinkResponse[]>(route))!));
        using var stranger = await factory.OwnerClientAsync(other);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reopened.GetAsync($"/api/{resource}/{Guid.NewGuid()}/share-links")).StatusCode);
    }
}
