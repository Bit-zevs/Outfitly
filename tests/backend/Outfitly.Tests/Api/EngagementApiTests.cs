using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
namespace Outfitly.Tests.Api;

public sealed class EngagementApiTests
{
    [Fact]
    public async Task Real_sessions_confirmation_csrf_and_unique_reactions_are_enforced()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var owner = await Register(factory, "owner");
        var created = await owner.PostAsJsonAsync("/api/items", new { name = "Coat", category = "Outerwear" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await owner.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        using var guest = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/items/{id}/engagement")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsync($"/api/items/{id}/views", null)).StatusCode);
        await ApiFactory.SetCsrfAsync(guest);
        (await guest.PostAsJsonAsync("/api/auth/register", new { email = "pending@example.com", username = "pending", displayName = "Pending", password = "long password 123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PostAsJsonAsync("/api/auth/login", new { email = "pending@example.com", password = "long password 123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsync($"/api/items/{id}/like", null)).StatusCode);
        using var viewer = await Register(factory, "viewer");
        viewer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsync($"/api/items/{id}/views", null)).StatusCode);
        await ApiFactory.SetCsrfAsync(viewer);
        foreach (var client in new[] { owner, viewer, viewer })
        {
            (await client.PostAsync($"/api/items/{id}/views", null)).EnsureSuccessStatusCode();
            (await client.PutAsync($"/api/items/{id}/like", null)).EnsureSuccessStatusCode();
        }
        var stats = await guest.GetFromJsonAsync<EngagementView>($"/api/items/{id}/engagement");
        Assert.Equal(1, stats!.Views); Assert.Equal(2, stats.Likes); Assert.False(stats.CanInteract);
        (await viewer.DeleteAsync($"/api/items/{id}/like")).EnsureSuccessStatusCode();
        Assert.Equal(1, (await guest.GetFromJsonAsync<EngagementView>($"/api/items/{id}/engagement"))!.Likes);
        (await viewer.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.PutAsync($"/api/items/{id}/like", null)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Equal(1, await db.Set<PostInteraction>().CountAsync(x => x.Kind == "view"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Private_links_comments_permissions_replies_and_deletion(bool outfit)
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var ownerId = Guid.NewGuid(); var visitorId = Guid.NewGuid(); var strangerId = Guid.NewGuid();
        await factory.SeedOwnerAsync(ownerId); await factory.SeedOwnerAsync(visitorId); await factory.SeedOwnerAsync(strangerId);
        using var owner = await factory.OwnerClientAsync(ownerId);
        using var visitor = await factory.OwnerClientAsync(visitorId);
        using var stranger = await factory.OwnerClientAsync(strangerId);
        var itemResponse = await owner.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" });
        itemResponse.EnsureSuccessStatusCode();
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var id = itemId; var kind = outfit ? "outfits" : "items";
        if (outfit)
        {
            var result = await owner.PostAsJsonAsync("/api/outfits", new { name = "Look" }); result.EnsureSuccessStatusCode();
            id = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            (await owner.PostAsync($"/api/outfits/{id}/items/{itemId}", null)).EnsureSuccessStatusCode();
        }
        var basePath = $"/api/{kind}/{id}";
        var link = await owner.PostAsync(basePath + "/share-links", null); link.EnsureSuccessStatusCode();
        var linkData = await link.Content.ReadFromJsonAsync<JsonElement>();
        var token = "?shareToken=" + linkData.GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(basePath + "/engagement")).StatusCode);
        if (outfit) Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/items/{itemId}/engagement" + token)).StatusCode);
        var root = await Add(visitor, basePath + "/comments" + token, "  hello  ");
        var reply = await Add(owner, basePath + "/comments", "reply", root);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.PostAsJsonAsync(basePath + "/comments" + token, new { text = "nested", parentId = reply })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.PostAsJsonAsync(basePath + "/comments" + token, new { text = new string('a', 2001) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PatchAsJsonAsync(basePath + $"/comments/{root}" + token, new { text = "hijack" })).StatusCode);
        (await visitor.PatchAsJsonAsync(basePath + $"/comments/{root}" + token, new { text = "<script>edited</script>" })).EnsureSuccessStatusCode();
        var comments = await visitor.GetFromJsonAsync<PublicPage<CommentView>>(basePath + "/comments" + token);
        Assert.Equal("<script>edited</script>", Assert.Single(comments!.Items).Text);
        Assert.NotNull(Assert.Single(comments.Items).UpdatedAt);
        (await owner.DeleteAsync(basePath + $"/comments/{root}")).EnsureSuccessStatusCode();
        var deleted = Assert.Single((await visitor.GetFromJsonAsync<PublicPage<CommentView>>(basePath + "/comments" + token))!.Items);
        Assert.True(deleted.IsDeleted); Assert.Null(deleted.Text);
        Assert.Single((await visitor.GetFromJsonAsync<PublicPage<CommentView>>(basePath + "/comments" + token + "&parentId=" + root))!.Items);
        (await owner.DeleteAsync("/api/share-links/" + linkData.GetProperty("id").GetGuid())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(basePath + "/comments" + token)).StatusCode);
        (await owner.PatchAsJsonAsync(basePath + "/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        Assert.Equal(1, (await visitor.GetFromJsonAsync<EngagementView>(basePath + "/engagement"))!.Comments);
        (await owner.DeleteAsync(basePath)).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Empty(await db.Set<PostInteraction>().ToListAsync());
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("unconfirmed")]
    public async Task Every_reaction_revalidates_the_database_session(string reason)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = await Register(factory, "actor");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            if (reason == "unconfirmed") (await db.Accounts.SingleAsync()).EmailConfirmed = false;
            else
            {
                var session = await db.LoginSessions.SingleAsync();
                if (reason == "revoked") session.IsRevoked = true;
                else db.Entry(session).Property(x => x.ExpiresAtUtc).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
            }
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
        var path = $"/api/items/{Guid.NewGuid()}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path + "/views", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync(path + "/like", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path + "/comments", new { text = "forged", actorId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Comments_and_replies_have_independent_stable_pages_and_validate_targets()
    {
        using var factory = new ApiFactory(); await factory.InitializeDatabaseAsync();
        var ownerId = Guid.NewGuid(); await factory.SeedOwnerAsync(ownerId);
        using var owner = await factory.OwnerClientAsync(ownerId);
        var result = await owner.PostAsJsonAsync("/api/items", new { name = "Top", category = "Top" }); result.EnsureSuccessStatusCode();
        var id = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/items/{id}/comments";
        var roots = new List<Guid>();
        for (var i = 0; i < 21; i++) roots.Add(await Add(owner, path, "Root " + i));
        for (var i = 0; i < 21; i++) await Add(owner, path, "Reply " + i, roots[0]);
        var first = (await owner.GetFromJsonAsync<PublicPage<CommentView>>(path))!;
        var second = (await owner.GetFromJsonAsync<PublicPage<CommentView>>(path + "?page=2"))!;
        Assert.Equal(21, first.Total); Assert.Equal(20, first.Items.Count);
        Assert.Equal(roots, first.Items.Concat(second.Items).Select(x => x.Id));
        var replies = (await owner.GetFromJsonAsync<PublicPage<CommentView>>(path + "?parentId=" + roots[0]))!;
        Assert.Equal(21, replies.Total); Assert.All(replies.Items, x => Assert.Equal(roots[0], x.ParentId));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(path + "?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new { text = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new { text = "Reply", parentId = Guid.NewGuid() })).StatusCode);
    }
    private static async Task<Guid> Add(HttpClient client, string path, string text, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync(path, new { text, parentId }); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<HttpClient> Register(ApiFactory factory, string name)
    {
        var client = factory.CreateClient(); await ApiFactory.SetCsrfAsync(client);
        (await client.PostAsJsonAsync("/api/auth/register", new { email = name + "@example.com", username = name, displayName = name, password = "long password 123" })).EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(client, name + "@example.com");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = name + "@example.com", password = "long password 123" })).EnsureSuccessStatusCode();
        await ApiFactory.SetCsrfAsync(client); return client;
    }
}
