using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Api.Contracts;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
using SkiaSharp;

namespace Outfitly.Tests.Api;

public sealed class PhotoApiTests
{
    internal static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 2)
    {
        using var bitmap = new SKBitmap(width, 3);
        bitmap.Erase(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    internal static async Task<HttpResponseMessage> Upload(HttpClient client, Guid id, byte[]? bytes = null, string mime = "image/png")
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes ?? Image());
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(content, "file", "../../client-name.png");
        return await client.PutAsync($"/api/items/{id}/photo", form);
    }

    internal static async Task<Guid> CreateItem(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    [Fact]
    public async Task Upload_replace_edit_external_url_and_delete_preserve_saved_state_and_clean_files()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        var id = await CreateItem(client);
        var uploaded = await Upload(client, id);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var body = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        var url = body.GetProperty("photoUrl").GetString()!;
        Assert.Equal($"/api/items/{id}/photo", url);
        Assert.DoesNotContain("StorageKey", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var firstFile = Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        var read = await client.GetAsync(url);
        Assert.Equal("image/png", read.Content.Headers.ContentType?.MediaType);
        Assert.True(read.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(read.Headers.GetValues("X-Content-Type-Options")));
        using (var scope = factory.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().WardrobeItems.SingleAsync();
            Assert.NotNull(saved.PhotoStorageKey);
            Assert.Null(saved.PhotoUrl);
        }
        (await client.PutAsJsonAsync($"/api/items/{id}", new { name = "Renamed", category = "Top" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        (await client.PutAsJsonAsync($"/api/items/{id}", new { name = "Renamed again", category = "Top", photoUrl = url })).EnsureSuccessStatusCode();
        (await Upload(client, id, Image(SKEncodedImageFormat.Jpeg), "image/jpeg")).EnsureSuccessStatusCode();
        Assert.False(File.Exists(firstFile));
        Assert.EndsWith(".jpg", Assert.Single(Directory.GetFiles(factory.PhotoRoot)));
        (await client.PutAsJsonAsync($"/api/items/{id}", new { name = "External", category = "Top", photoUrl = "https://example.com/photo.jpg" })).EnsureSuccessStatusCode();
        Assert.Empty(Directory.GetFiles(factory.PhotoRoot));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        (await Upload(client, id)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
        Assert.Empty(Directory.GetFiles(factory.PhotoRoot));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
        (await Upload(client, id)).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/items/{id}")).EnsureSuccessStatusCode();
        Assert.Empty(Directory.GetFiles(factory.PhotoRoot));
    }

    [Fact]
    public async Task Photo_access_follows_publication_sharing_and_outfit_membership()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        await factory.SeedOwnerAsync(other);
        using var client = await factory.OwnerClientAsync(owner);
        using var stranger = await factory.OwnerClientAsync(other);
        using var anonymous = factory.CreateClient();
        var id = await CreateItem(client);
        (await Upload(client, id)).EnsureSuccessStatusCode();
        var ownUrl = $"/api/items/{id}/photo";
        var publicUrl = $"/api/wardrobe/items/{id}/photo";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ownUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(ownUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(stranger, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.DeleteAsync(ownUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(publicUrl)).StatusCode);
        var itemLink = (await (await client.PostAsync($"/api/items/{id}/share-links", null)).Content.ReadFromJsonAsync<ShareLinkResponse>())!;
        var sharedItem = await anonymous.GetFromJsonAsync<JsonElement>(itemLink.Url);
        var itemPhotoUrl = sharedItem.GetProperty("photoUrl").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(itemPhotoUrl)).StatusCode);
        (await client.DeleteAsync($"/api/share-links/{itemLink.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(itemPhotoUrl)).StatusCode);
        await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true });
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(publicUrl)).StatusCode);
        var catalog = await anonymous.GetFromJsonAsync<JsonElement>("/api/wardrobe");
        Assert.Equal(publicUrl, Assert.Single(catalog.GetProperty("items").EnumerateArray()).GetProperty("photoUrl").GetString());
        await client.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = false });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(publicUrl)).StatusCode);
        var outfit = await (await client.PostAsJsonAsync("/api/outfits", new { name = "Daily" })).Content.ReadFromJsonAsync<JsonElement>();
        var outfitId = outfit.GetProperty("id").GetGuid();
        await client.PostAsync($"/api/outfits/{outfitId}/items/{id}", null);
        var outfitLink = (await (await client.PostAsync($"/api/outfits/{outfitId}/share-links", null)).Content.ReadFromJsonAsync<ShareLinkResponse>())!;
        var shared = await anonymous.GetFromJsonAsync<JsonElement>(outfitLink.Url);
        var sharedPhoto = Assert.Single(shared.GetProperty("items").EnumerateArray()).GetProperty("photoUrl").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(sharedPhoto)).StatusCode);
        await client.PatchAsJsonAsync($"/api/outfits/{outfitId}/publication", new { isPublic = true });
        var publicOutfitPhoto = $"/api/wardrobe/outfits/{outfitId}/items/{id}/photo";
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(publicOutfitPhoto)).StatusCode);
        await client.PatchAsJsonAsync($"/api/outfits/{outfitId}/publication", new { isPublic = false });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(publicOutfitPhoto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(sharedPhoto)).StatusCode);
        await client.DeleteAsync($"/api/outfits/{outfitId}/items/{id}");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(sharedPhoto)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(publicOutfitPhoto)).StatusCode);
    }

    [Fact]
    public async Task Invalid_uploads_and_missing_csrf_do_not_replace_the_existing_photo()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        var id = await CreateItem(client);
        (await Upload(client, id)).EnsureSuccessStatusCode();
        var original = Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, [1, 2, 3])).StatusCode);
        var truncated = Image();
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, truncated[..(truncated.Length / 2)])).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(client, id, mime: "image/svg+xml")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(client, id, mime: "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(client, id, new byte[PhotoService.MaxBytes + 1])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, Image(width: 8193))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/items/{id}/photo", new { file = "fake" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/items/{id}", new { name = "Stolen", category = "Top", photoUrl = $"/api/items/{Guid.NewGuid()}/photo" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/items/{id}/photo")).StatusCode);
        Assert.Equal(original, Assert.Single(Directory.GetFiles(factory.PhotoRoot)));
    }

    [Fact]
    public async Task Failed_commit_removes_the_new_file_and_keeps_the_previous_photo()
    {
        var failure = new FailNextSave();
        using var factory = new ApiFactory(configureDatabase: options => options.AddInterceptors(failure));
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        var id = await CreateItem(client);
        (await Upload(client, id)).EnsureSuccessStatusCode();
        var original = Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        failure.Enabled = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await Upload(client, id)).StatusCode);
        Assert.Equal(original, Assert.Single(Directory.GetFiles(factory.PhotoRoot)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/items/{id}/photo")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().WardrobeItems.SingleAsync();
        Assert.Equal(Path.GetFileName(original), saved.PhotoStorageKey);
    }

    private sealed class FailNextSave : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Enabled = false;
                ((OutfitlyDbContext)eventData.Context!).WardrobeItems.Add(new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "Invalid owner", ClothingCategory.Top));
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Concurrent_HTTP_upload_returns_409_and_removes_only_the_losing_file()
    {
        var beforeCommit = new BeforeCommit();
        using var factory = new ApiFactory(configureDatabase: options => options.AddInterceptors(beforeCommit));
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        var id = await CreateItem(client);
        (await Upload(client, id)).EnsureSuccessStatusCode();
        beforeCommit.Action = async () =>
        {
            using var otherClient = await factory.OwnerClientAsync(owner);
            (await Upload(otherClient, id, Image(SKEncodedImageFormat.Jpeg), "image/jpeg")).EnsureSuccessStatusCode();
        };
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, id)).StatusCode);
        var winningFile = Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        Assert.EndsWith(".jpg", winningFile);
        var photo = await client.GetAsync($"/api/items/{id}/photo");
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(Path.GetFileName(winningFile), (await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().WardrobeItems.SingleAsync()).PhotoStorageKey);
    }

    private sealed class BeforeCommit : SaveChangesInterceptor
    {
        internal Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var action = Action;
            Action = null;
            if (action is not null) await action();
            return result;
        }
    }
}
