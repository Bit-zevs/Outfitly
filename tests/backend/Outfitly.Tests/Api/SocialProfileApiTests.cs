using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
using SkiaSharp;

namespace Outfitly.Tests.Api;

public sealed class SocialProfileApiTests
{
    [Fact]
    public async Task Bio_is_public_validated_and_preserved_by_legacy_profile_requests()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var client = await Register(factory, "anna");
        var path = "/api/auth/me/profile";
        (await client.PatchAsJsonAsync(path, new { displayName = "Anna", username = "anna", bio = "  First line\n<script>text</script>  " })).EnsureSuccessStatusCode();
        var profile = await client.GetFromJsonAsync<PublicProfile>("/api/users/anna");
        Assert.Equal("First line\n<script>text</script>", profile!.Bio);
        (await client.PatchAsJsonAsync(path, new { displayName = "New Anna", username = "anna" })).EnsureSuccessStatusCode();
        Assert.Equal(profile.Bio, (await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!.Bio);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path, new { displayName = "Anna", username = "anna", bio = new string('a', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path, new { displayName = "Anna", username = "anna", bio = 12 })).StatusCode);
        (await client.PatchAsJsonAsync(path, new { displayName = "Anna", username = "anna", bio = (string?)null })).EnsureSuccessStatusCode();
        Assert.Null((await client.GetFromJsonAsync<PublicProfile>("/api/users/anna"))!.Bio);
        using var scope = factory.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Users.SingleAsync()).Bio);
    }

    [Fact]
    public async Task Following_requires_a_real_confirmed_session_and_csrf_and_survives_username_changes()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var anna = await Register(factory, "anna"); using var bob = await Register(factory, "bobby"); using var guest = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsync("/api/users/anna/follow", null)).StatusCode);
        await ApiFactory.SetCsrfAsync(guest);
        (await guest.PostAsJsonAsync("/api/auth/register", new { email = "pending@example.com", username = "pending", displayName = "Pending", password = "long password 123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsync("/api/users/pending/follow", null)).StatusCode);
        bob.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PutAsync("/api/users/anna/follow", null)).StatusCode);
        await ApiFactory.SetCsrfAsync(bob);
        Assert.Equal(HttpStatusCode.BadRequest, (await anna.PutAsync("/api/users/anna/follow", null)).StatusCode);
        for (var i = 0; i < 2; i++) (await bob.PutAsync("/api/users/anna/follow", null)).EnsureSuccessStatusCode();
        var profile = (await bob.GetFromJsonAsync<PublicProfile>("/api/users/anna"))!;
        Assert.Equal(1, profile.FollowersCount); Assert.True(profile.IsFollowing); Assert.True(profile.CanFollow);
        Assert.False((await guest.GetFromJsonAsync<PublicProfile>("/api/users/anna"))!.CanFollow);
        var followers = (await guest.GetFromJsonAsync<PublicPage<PublicUser>>("/api/users/anna/followers"))!;
        Assert.Equal("bobby", Assert.Single(followers.Items).Username);
        Assert.DoesNotContain("email", await (await guest.GetAsync("/api/users/anna/followers")).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, (await guest.GetFromJsonAsync<PublicProfile>("/api/users/bobby"))!.FollowingCount);
        (await anna.PatchAsJsonAsync("/api/auth/me/profile", new { displayName = "Anna", username = "new_anna" })).EnsureSuccessStatusCode();
        Assert.True((await bob.GetFromJsonAsync<PublicProfile>("/api/users/new_anna"))!.IsFollowing);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsync("/api/users/anna/follow", null)).StatusCode);
        for (var i = 0; i < 2; i++) (await bob.DeleteAsync("/api/users/new_anna/follow")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await guest.GetFromJsonAsync<PublicProfile>("/api/users/new_anna"))!.FollowersCount);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            var account = await db.Accounts.SingleAsync(a => a.Email == "bobby@example.com");
            account.EmailConfirmed = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.PutAsync("/api/users/new_anna/follow", null)).StatusCode);
    }

    [Fact]
    public async Task Avatar_upload_is_public_replaces_and_deletes_files_without_exposing_storage_keys()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var client = await Register(factory, "anna");
        (await Upload(client)).EnsureSuccessStatusCode();
        var me = (await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!;
        Assert.NotNull(me.AvatarUrl);
        using var guest = factory.CreateClient();
        var imageResponse = await guest.GetAsync(me.AvatarUrl);
        imageResponse.EnsureSuccessStatusCode(); Assert.Equal("no-store", imageResponse.Headers.CacheControl!.ToString());
        using var bitmap = SKBitmap.Decode(await imageResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(512, bitmap.Width); Assert.Equal(512, bitmap.Height);
        Assert.True(bitmap.GetPixel(256, 256).Blue > 240);
        var first = Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        Assert.DoesNotContain(Path.GetFileName(first), await (await client.GetAsync("/api/auth/me")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("avatarStorageKey", await (await guest.GetAsync("/api/users/anna")).Content.ReadAsStringAsync());
        (await Upload(client)).EnsureSuccessStatusCode();
        Assert.False(File.Exists(first)); Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        Assert.NotNull(Assert.Single((await guest.GetFromJsonAsync<PublicPage<PublicUser>>("/api/users?search=anna"))!.Items).AvatarUrl);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, cropX: 999)).StatusCode);
        Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(client, type: "image/gif")).StatusCode);
        (await client.DeleteAsync("/api/auth/me/avatar")).EnsureSuccessStatusCode();
        Assert.Empty(Directory.GetFiles(factory.PhotoRoot));
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(me.AvatarUrl)).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!.AvatarUrl);
    }

    [Fact]
    public async Task A_losing_avatar_upload_returns_409_and_cleans_only_its_own_file()
    {
        var before = new BeforeCommit();
        using var factory = new ApiFactory(testAuthentication: false, configureDatabase: o => o.AddInterceptors(before)); await factory.InitializeDatabaseAsync();
        using var client = await Register(factory, "anna");
        (await Upload(client)).EnsureSuccessStatusCode();
        before.Action = async () => { (await Upload(client)).EnsureSuccessStatusCode(); };
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client)).StatusCode);
        var remaining = Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(Path.GetFileName(remaining), (await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Users.SingleAsync()).AvatarStorageKey);
    }

    [Fact]
    public async Task Failed_avatar_commit_preserves_the_previous_file()
    {
        var before = new BeforeCommit();
        using var factory = new ApiFactory(testAuthentication: false, configureDatabase: o => o.AddInterceptors(before)); await factory.InitializeDatabaseAsync();
        using var client = await Register(factory, "anna");
        (await Upload(client)).EnsureSuccessStatusCode();
        var previous = Assert.Single(Directory.GetFiles(factory.PhotoRoot));
        before.Action = () => throw new InvalidOperationException("Test save failure");
        Assert.Equal(HttpStatusCode.InternalServerError, (await Upload(client)).StatusCode);
        Assert.Equal(previous, Assert.Single(Directory.GetFiles(factory.PhotoRoot)));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(Path.GetFileName(previous), (await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Users.SingleAsync()).AvatarStorageKey);
    }

    [Fact]
    public async Task Avatar_changes_require_csrf_and_an_unrevoked_session_and_unconfirmed_avatars_are_hidden()
    {
        using var factory = new ApiFactory(testAuthentication: false); await factory.InitializeDatabaseAsync();
        using var client = await Register(factory, "anna"); using var guest = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(guest)).StatusCode);
        (await Upload(client)).EnsureSuccessStatusCode();
        var url = (await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!.AvatarUrl;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client)).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            (await db.LoginSessions.SingleAsync()).IsRevoked = true; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/auth/me/avatar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(url)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            (await db.Accounts.SingleAsync()).EmailConfirmed = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(url)).StatusCode);
        Assert.Single(Directory.GetFiles(factory.PhotoRoot));
    }
    internal static async Task<HttpClient> Register(ApiFactory factory, string name)
    {
        var client = factory.CreateClient(); await ApiFactory.SetCsrfAsync(client);
        (await client.PostAsJsonAsync("/api/auth/register", new { email = name + "@example.com", username = name, displayName = name, password = "long password 123" })).EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(client, name + "@example.com");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = name + "@example.com", password = "long password 123" })).EnsureSuccessStatusCode();
        await ApiFactory.SetCsrfAsync(client); return client;
    }
    internal static async Task<HttpResponseMessage> Upload(HttpClient client, int cropX = 20, string type = "image/png")
    {
        using var bitmap = new SKBitmap(40, 20); bitmap.Erase(SKColors.Red);
        using (var canvas = new SKCanvas(bitmap)) { using var brush = new SKPaint { Color = SKColors.Blue }; canvas.DrawRect(20, 0, 20, 20, brush); }
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var body = new MultipartFormDataContent(); var bytes = new ByteArrayContent(encoded.ToArray()); bytes.Headers.ContentType = new(type);
        body.Add(bytes, "file", "avatar.png"); body.Add(new StringContent(cropX.ToString()), "cropX"); body.Add(new StringContent("0"), "cropY"); body.Add(new StringContent("20"), "cropSize");
        return await client.PutAsync("/api/auth/me/avatar", body);
    }
    private sealed class BeforeCommit : SaveChangesInterceptor
    {
        internal Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var action = Action; Action = null; if (action is not null) await action(); return result;
        }
    }
}
