using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
namespace Outfitly.Tests.Api;
public sealed class WorkspaceApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_item_visibility_is_saved_only_on_explicit_draft_completion(bool isPublic)
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var owner = await SocialProfileApiTests.Register(factory, "visibility");
        var id = Guid.NewGuid();
        var draft = await PutDraft(owner, id, new("items", null, null, null,
            new(Name: "Coat", Category: ClothingCategory.Outerwear, IsPublic: isPublic)));
        Assert.Equal(isPublic, (await owner.GetFromJsonAsync<DraftView>($"/api/drafts/{id}", Json))!.Content.IsPublic);
        using (var before = factory.Services.CreateScope())
            Assert.Empty(await before.ServiceProvider.GetRequiredService<OutfitlyDbContext>().WardrobeItems.ToArrayAsync());
        var saved = await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = draft.Version });
        saved.EnsureSuccessStatusCode();
        using var after = factory.Services.CreateScope();
        var item = Assert.Single(await after.ServiceProvider.GetRequiredService<OutfitlyDbContext>().WardrobeItems.ToArrayAsync());
        Assert.Equal(isPublic, item.IsPublic);
    }
    [Fact]
    public async Task New_outfit_can_be_published_atomically_with_its_members()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var owner = await SocialProfileApiTests.Register(factory, "publishoutfit");
        using var guest = factory.CreateClient();
        var item = await CreateItem(owner, "Shirt");
        var draftId = Guid.NewGuid();
        var draft = await PutDraft(owner, draftId, new("outfits", null, null, null,
            new(Name: "Look", ItemIds: [item], IsPublic: true)));
        var saved = await owner.PostAsJsonAsync($"/api/drafts/{draftId}/save", new { version = draft.Version });
        saved.EnsureSuccessStatusCode();
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var outfit = (await guest.GetFromJsonAsync<OutfitView>($"/api/wardrobe/outfits/{id}", Json))!;
        Assert.True(outfit.IsPublic);
        Assert.Equal(item, Assert.Single(outfit.Items).Id);
    }

    [Theory]
    [InlineData("https://localhost/api/items/example/photo", HttpStatusCode.BadRequest)]
    [InlineData("https://localhost/a/../%61pi/items/example/photo", HttpStatusCode.BadRequest)]
    [InlineData("https://localhost:7043/api/items/example/photo", HttpStatusCode.BadRequest)]
    [InlineData("https://example.com/api/images/coat.png", HttpStatusCode.OK)]
    public async Task Draft_completion_enforces_the_same_photo_url_boundary(string url, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        var actor = Guid.NewGuid();
        await factory.SeedOwnerAsync(actor);
        using var client = await factory.OwnerClientAsync(actor);
        var id = Guid.NewGuid();
        var draft = await PutDraft(client, id, new("items", null, null, null,
            new(Name: "Coat", Category: ClothingCategory.Outerwear, PhotoUrl: url, PhotoMode: "url")));

        var saved = await client.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = draft.Version });

        Assert.Equal(expected, saved.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        var items = await db.WardrobeItems.AsNoTracking().ToListAsync();
        if (expected == HttpStatusCode.OK) Assert.Equal(url, Assert.Single(items).PhotoUrl);
        else Assert.Empty(items);
    }

    [Fact]
    public async Task Copy_commands_reject_excess_photos_before_writing_and_share_an_account_quota()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        var reader = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        await factory.SeedOwnerAsync(reader);
        using var seller = await factory.OwnerClientAsync(owner);
        using var buyer = await factory.OwnerClientAsync(reader);
        var original = await CreateItem(seller, "Original");
        (await seller.PutAsync($"/api/items/{original}/photo", ImageForm())).EnsureSuccessStatusCode();

        var largeId = Guid.NewGuid();
        var allowedId = Guid.NewGuid();
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            var photo = (await db.WardrobeItems.SingleAsync()).PhotoStorageKey!;
            var large = new Outfit(largeId, owner, "Large");
            var allowed = new Outfit(allowedId, owner, "Allowed");
            for (var index = 0; index < 21; index++)
            {
                var item = new WardrobeItem(Guid.NewGuid(), owner, $"Item {index}", ClothingCategory.Top);
                item.SetUploadedPhoto(photo);
                large.AddItem(item);
                if (index < 20) allowed.AddItem(item);
            }
            large.Publish();
            allowed.Publish();
            db.Outfits.AddRange(large, allowed);
            await db.SaveChangesAsync();
        }

        var rejected = await buyer.PostAsJsonAsync($"/api/outfits/{largeId}/copies", new { operationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Single(Directory.GetFiles(factory.PhotoRoot));

        var last = Guid.Empty;
        for (var index = 0; index < 5; index++)
        {
            last = Guid.NewGuid();
            (await buyer.PostAsJsonAsync($"/api/outfits/{allowedId}/copies", new { operationId = last })).EnsureSuccessStatusCode();
        }
        (await buyer.PostAsJsonAsync($"/api/outfits/{allowedId}/copies", new { operationId = last })).EnsureSuccessStatusCode();
        (await seller.PatchAsJsonAsync($"/api/items/{original}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        rejected = await buyer.PostAsJsonAsync($"/api/items/{original}/copies", new { operationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(101, Directory.GetFiles(factory.PhotoRoot).Length);
    }

    [Fact]
    public async Task Public_outfit_discussion_never_exposes_or_accepts_replies_to_private_link_comments()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        var visitor = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        await factory.SeedOwnerAsync(visitor);
        using var seller = await factory.OwnerClientAsync(owner);
        using var reader = await factory.OwnerClientAsync(visitor);
        using var guest = factory.CreateClient();
        var item = await CreateItem(seller, "Coat");
        var outfit = await CreateOutfit(seller, item);
        var link = await seller.PostAsync($"/api/items/{item}/share-links", null);
        link.EnsureSuccessStatusCode();
        var token = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var target = $"/api/items/{item}";
        var restricted = await reader.PostAsJsonAsync($"{target}/comments?shareToken={token}", new { text = "Restricted" });
        restricted.EnsureSuccessStatusCode();
        var parentId = (await restricted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await seller.PatchAsJsonAsync($"/api/outfits/{outfit}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        var publicContext = $"?publicOutfitId={outfit}";
        Assert.Equal(0, (await guest.GetFromJsonAsync<EngagementView>($"{target}/engagement{publicContext}"))!.Comments);
        Assert.Empty((await guest.GetFromJsonAsync<PublicPage<CommentView>>($"{target}/comments{publicContext}"))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.PostAsJsonAsync($"{target}/comments{publicContext}",
            new { text = "Cross-audience reply", parentId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.PatchAsJsonAsync($"{target}/comments/{parentId}{publicContext}",
            new { text = "Cross-audience edit" })).StatusCode);

        (await reader.PostAsJsonAsync($"{target}/comments{publicContext}", new { text = "Public" })).EnsureSuccessStatusCode();
        Assert.Equal("Public", Assert.Single((await guest.GetFromJsonAsync<PublicPage<CommentView>>(
            $"{target}/comments{publicContext}"))!.Items).Text);
        Assert.Equal("Restricted", Assert.Single((await reader.GetFromJsonAsync<PublicPage<CommentView>>(
            $"{target}/comments?shareToken={token}"))!.Items).Text);
    }

    [Fact]
    public async Task Favorites_recheck_access_keep_private_tombstones_and_never_share_another_accounts_list()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var owner = await SocialProfileApiTests.Register(factory, "owner"); using var reader = await SocialProfileApiTests.Register(factory, "reader"); using var guest = factory.CreateClient();
        var item = await CreateItem(owner, "Secret name");
        var linkResponse = await owner.PostAsync($"/api/items/{item}/share-links", null); linkResponse.EnsureSuccessStatusCode(); var link = await linkResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = link.GetProperty("token").GetString(); var path = $"/api/items/{item}/favorite";
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/favorites")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.PutAsync(path, null)).StatusCode);
        for (var i = 0; i < 2; i++) (await reader.PutAsync(path + "?shareToken=" + token, null)).EnsureSuccessStatusCode();
        var page = (await reader.GetFromJsonAsync<PublicPage<FavoriteView>>("/api/favorites?kind=items"))!;
        Assert.Equal("Secret name", Assert.Single(page.Items).Name); Assert.Contains(token!, Assert.Single(page.Items).Route!);
        Assert.Empty((await owner.GetFromJsonAsync<PublicPage<FavoriteView>>("/api/favorites?kind=items"))!.Items);
        (await owner.DeleteAsync("/api/share-links/" + link.GetProperty("id").GetGuid())).EnsureSuccessStatusCode();
        var hidden = Assert.Single((await reader.GetFromJsonAsync<PublicPage<FavoriteView>>("/api/favorites?kind=items"))!.Items);
        Assert.False(hidden.Available); Assert.Null(hidden.Name); Assert.Null(hidden.PhotoUrl); Assert.Null(hidden.Route);
        (await owner.PatchAsJsonAsync($"/api/items/{item}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        Assert.True(Assert.Single((await reader.GetFromJsonAsync<PublicPage<FavoriteView>>("/api/favorites?kind=items"))!.Items).Available);
        (await owner.DeleteAsync($"/api/items/{item}")).EnsureSuccessStatusCode();
        Assert.False(Assert.Single((await reader.GetFromJsonAsync<PublicPage<FavoriteView>>("/api/favorites?kind=items"))!.Items).Available);
        for (var i = 0; i < 2; i++) (await reader.DeleteAsync(path)).EnsureSuccessStatusCode();
        Assert.Empty((await reader.GetFromJsonAsync<PublicPage<FavoriteView>>("/api/favorites?kind=items"))!.Items);
    }
    [Fact]
    public async Task Copies_are_private_independent_and_idempotent_including_their_local_photo_files()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var owner = await SocialProfileApiTests.Register(factory, "owner"); using var reader = await SocialProfileApiTests.Register(factory, "reader");
        var item = await CreateItem(owner, "Original");
        (await owner.PutAsync($"/api/items/{item}/photo", ImageForm())).EnsureSuccessStatusCode();
        var outfit = await CreateOutfit(owner, item);
        var external = await CreateItem(owner, "External");
        (await owner.PutAsJsonAsync($"/api/items/{external}", new { name = "External", category = "Top", photoUrl = "https://example.com/shirt.png" })).EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/outfits/{outfit}/items/{external}", null)).EnsureSuccessStatusCode();
        (await owner.PatchAsJsonAsync($"/api/outfits/{outfit}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        var operationId = Guid.NewGuid(); Guid copyId = default;
        for (var i = 0; i < 2; i++) { var response = await reader.PostAsJsonAsync($"/api/outfits/{outfit}/copies", new { operationId }); response.EnsureSuccessStatusCode(); var actual = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); if (i == 0) copyId = actual; else Assert.Equal(copyId, actual); }
        var copy = (await reader.GetFromJsonAsync<OutfitView>($"/api/outfits/{copyId}", Json))!; Assert.False(copy.IsPublic); Assert.EndsWith("(копия)", copy.Name);
        var cloned = Assert.Single(copy.Items, x => x.Name == "Original"); Assert.NotEqual(item, cloned.Id); Assert.False(cloned.IsPublic);
        var clonedExternal = Assert.Single(copy.Items, x => x.Name == "External");
        Assert.NotEqual(external, clonedExternal.Id); Assert.Equal("https://example.com/shirt.png", clonedExternal.PhotoUrl); Assert.False(clonedExternal.IsPublic);
        Assert.Equal(2, Directory.GetFiles(factory.PhotoRoot).Length);
        (await owner.DeleteAsync($"/api/items/{item}")).EnsureSuccessStatusCode(); (await owner.DeleteAsync($"/api/outfits/{outfit}")).EnsureSuccessStatusCode();
        Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/items/{cloned.Id}/photo")).StatusCode);
        var retry = await reader.PostAsJsonAsync($"/api/outfits/{outfit}/copies", new { operationId }); retry.EnsureSuccessStatusCode();
        Assert.Equal(copyId, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }
    [Fact]
    public async Task Public_outfit_members_can_be_copied_and_discussed_only_with_valid_context()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var owner = await SocialProfileApiTests.Register(factory, "owner");
        using var reader = await SocialProfileApiTests.Register(factory, "reader");
        var item = await CreateItem(owner, "Coat");
        var unrelated = await CreateItem(owner, "Other");
        var outfit = await CreateOutfit(owner, item);
        var query = $"?publicOutfitId={outfit}";
        var operation = new { operationId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.NotFound, (await reader.PostAsJsonAsync($"/api/items/{item}/copies{query}", operation)).StatusCode);
        (await owner.PatchAsJsonAsync($"/api/outfits/{outfit}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/items/{unrelated}/engagement{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/items/{item}/engagement")).StatusCode);
        (await reader.PutAsync($"/api/items/{item}/like{query}", null)).EnsureSuccessStatusCode();
        (await reader.PostAsJsonAsync($"/api/items/{item}/comments{query}", new { text = "Nice coat" })).EnsureSuccessStatusCode();
        var counts = (await reader.GetFromJsonAsync<EngagementView>($"/api/items/{item}/engagement{query}"))!;
        Assert.True(counts.IsLiked);
        Assert.Equal(1, counts.Comments);
        var response = await reader.PostAsJsonAsync($"/api/items/{item}/copies{query}", operation);
        response.EnsureSuccessStatusCode();
        var copiedId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.NotEqual(item, copiedId);
        Assert.False((await reader.GetFromJsonAsync<ItemView>($"/api/items/{copiedId}", Json))!.IsPublic);
        var retry = await reader.PostAsJsonAsync($"/api/items/{item}/copies{query}", operation);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(copiedId, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        (await owner.PatchAsJsonAsync($"/api/outfits/{outfit}/publication", new { isPublic = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/items/{item}/engagement{query}")).StatusCode);
    }

    [Fact]
    public async Task Drafts_accept_incomplete_content_are_private_and_finalize_once_with_the_photo()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var owner = await SocialProfileApiTests.Register(factory, "owner"); using var other = await SocialProfileApiTests.Register(factory, "other");
        var id = Guid.NewGuid(); var initial = await PutDraft(owner, id, new("items", null, null, null, new()));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/drafts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = initial.Version })).StatusCode);
        var completedFields = await PutDraft(owner, id, new("items", null, initial.Version, null, new(Name: "Saved", Category: ClothingCategory.Top)));
        var photo = await owner.PutAsync($"/api/drafts/{id}/photo?version={completedFields.Version}", ImageForm()); photo.EnsureSuccessStatusCode();
        var withPhoto = (await photo.Content.ReadFromJsonAsync<DraftView>(Json))!;
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(withPhoto.PhotoUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/drafts/{id}", new DraftInput("items", null, initial.Version, null, new(Name: "Stale")))).StatusCode);
        var saved = await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = withPhoto.Version }); saved.EnsureSuccessStatusCode(); var result = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var repeat = await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = withPhoto.Version }); repeat.EnsureSuccessStatusCode(); Assert.Equal(result, (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Empty((await owner.GetFromJsonAsync<PublicPage<DraftView>>("/api/drafts"))!.Items);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/items/{result}/photo")).StatusCode); Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        using var scope = factory.Services.CreateScope(); Assert.Single(await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().WardrobeItems.ToArrayAsync());
    }
    [Fact]
    public async Task Saving_an_outfit_draft_is_atomic_and_stale_resource_versions_require_review()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync(); using var owner = await SocialProfileApiTests.Register(factory, "owner");
        var first = await CreateItem(owner, "First"); var second = await CreateItem(owner, "Second"); var outfit = await CreateOutfit(owner, first);
        var actual = (await owner.GetFromJsonAsync<OutfitView>($"/api/outfits/{outfit}", Json))!;
        var id = Guid.NewGuid(); var d = await PutDraft(owner, id, new("outfits", outfit, null, actual.EditVersion, new(Name: "Revised", ItemIds: [second, Guid.NewGuid()])));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = d.Version })).StatusCode);
        var unchanged = (await owner.GetFromJsonAsync<OutfitView>($"/api/outfits/{outfit}", Json))!; Assert.Equal(actual.Name, unchanged.Name); Assert.Equal(first, Assert.Single(unchanged.Items).Id);
        d = await PutDraft(owner, id, new("outfits", outfit, d.Version, actual.EditVersion, new(Name: "Revised", ItemIds: [second])));
        (await owner.PutAsJsonAsync($"/api/outfits/{outfit}", new { name = "Other device" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = d.Version })).StatusCode);
        var reviewed = (await owner.GetFromJsonAsync<OutfitView>($"/api/outfits/{outfit}", Json))!;
        d = await PutDraft(owner, id, new("outfits", outfit, d.Version, reviewed.EditVersion, new(Name: "Revised", ItemIds: [second])));
        (await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = d.Version })).EnsureSuccessStatusCode();
        Assert.Equal(second, Assert.Single((await owner.GetFromJsonAsync<OutfitView>($"/api/outfits/{outfit}", Json))!.Items).Id);
    }
    [Theory]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("unconfirmed")]
    public async Task Workspace_revalidates_sessions_and_csrf_on_all_commands(string reason)
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync(); using var client = await SocialProfileApiTests.Register(factory, "owner");
        var id = Guid.NewGuid(); var payload = new DraftInput("items", null, null, null, new());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/drafts/{id}", payload, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"/api/items/{id}/favorite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/outfits/{id}/copies", new { operationId = Guid.NewGuid() })).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            if (reason == "unconfirmed") (await db.Accounts.SingleAsync()).EmailConfirmed = false;
            else { var session = await db.LoginSessions.SingleAsync(); if (reason == "revoked") session.IsRevoked = true; else db.Entry(session).Property(x => x.ExpiresAtUtc).CurrentValue = DateTime.UtcNow.AddMinutes(-1); }
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/drafts/{id}", payload, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync($"/api/items/{id}/favorite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/outfits/{id}/copies", new { operationId = Guid.NewGuid() })).StatusCode);
    }
    [Fact]
    public async Task Staged_photo_changes_do_not_change_the_resource_until_an_explicit_save()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync(); using var owner = await SocialProfileApiTests.Register(factory, "owner");
        var item = await CreateItem(owner, "Original"); (await owner.PutAsync($"/api/items/{item}/photo", ImageForm())).EnsureSuccessStatusCode();
        var firstFile = Assert.Single(Directory.GetFiles(factory.PhotoRoot)); var baseline = (await owner.GetFromJsonAsync<ItemView>($"/api/items/{item}", Json))!;
        var id = Guid.NewGuid(); var d = await PutDraft(owner, id, new("items", item, null, baseline.EditVersion, new(Name: "Edited", Category: ClothingCategory.Top)));
        var response = await owner.PutAsync($"/api/drafts/{id}/photo?version={d.Version}", ImageForm()); response.EnsureSuccessStatusCode(); d = (await response.Content.ReadFromJsonAsync<DraftView>(Json))!;
        Assert.Equal(2, Directory.GetFiles(factory.PhotoRoot).Length); Assert.Equal("Original", (await owner.GetFromJsonAsync<ItemView>($"/api/items/{item}", Json))!.Name);
        (await owner.DeleteAsync($"/api/drafts/{id}?version={d.Version}")).EnsureSuccessStatusCode(); Assert.Equal(firstFile, Assert.Single(Directory.GetFiles(factory.PhotoRoot)));
        id = Guid.NewGuid(); d = await PutDraft(owner, id, new("items", item, null, baseline.EditVersion, new(Name: "Edited", Category: ClothingCategory.Top)));
        response = await owner.PutAsync($"/api/drafts/{id}/photo?version={d.Version}", ImageForm()); response.EnsureSuccessStatusCode(); d = (await response.Content.ReadFromJsonAsync<DraftView>(Json))!;
        (await owner.PostAsJsonAsync($"/api/drafts/{id}/save", new { version = d.Version })).EnsureSuccessStatusCode(); Assert.False(File.Exists(firstFile)); Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        Assert.Equal("Edited", (await owner.GetFromJsonAsync<ItemView>($"/api/items/{item}", Json))!.Name);
    }
    internal static async Task<DraftView> PutDraft(HttpClient client, Guid id, DraftInput input)
    { var response = await client.PutAsJsonAsync($"/api/drafts/{id}", input, Json); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<DraftView>(Json))!; }
    internal static async Task<Guid> CreateItem(HttpClient client, string name)
    { var response = await client.PostAsJsonAsync("/api/items", new { name, category = "Top" }); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }
    internal static async Task<Guid> CreateOutfit(HttpClient client, Guid item)
    { var response = await client.PostAsJsonAsync("/api/outfits", new { name = "Daily" }); response.EnsureSuccessStatusCode(); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); (await client.PostAsync($"/api/outfits/{id}/items/{item}", null)).EnsureSuccessStatusCode(); return id; }
    internal static MultipartFormDataContent ImageForm()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 4); using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var result = new MultipartFormDataContent(); var file = new ByteArrayContent(encoded.ToArray()); file.Headers.ContentType = new("image/png"); result.Add(file, "file", "photo.png"); return result;
    }
}
