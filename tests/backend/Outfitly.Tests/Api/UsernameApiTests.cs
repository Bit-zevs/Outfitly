using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Api;

public sealed class UsernameApiTests
{
    [Fact]
    public async Task Registration_and_renaming_use_normalized_unique_usernames_and_real_session_csrf()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await ApiFactory.SetCsrfAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        { email = "anna@example.com", password = "a long password", displayName = "Анна", username = " @Anna " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<AccountView>())!;
        Assert.Equal("anna", account.Username);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", new
        { email = "other@example.com", password = "a long password", displayName = "Other", username = "ANNA" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new
        { email = "other@example.com", password = "a long password", displayName = "Other" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new
        { email = "other@example.com", password = "a long password", displayName = "Other", username = "1invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsJsonAsync("/api/auth/me/username", new { username = "new_anna" })).StatusCode);
        await factory.ConfirmEmailAsync(client, "anna@example.com");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = "anna@example.com", password = "a long password" })).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/auth/me/username", new { username = "new_anna" })).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        foreach (var username in new[] { " @NEW_ANNA ", "new_anna" })
        {
            var changed = await client.PatchAsJsonAsync("/api/auth/me/username", new { username });
            changed.EnsureSuccessStatusCode();
            Assert.Equal("new_anna", (await changed.Content.ReadFromJsonAsync<AccountView>())!.Username);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users/anna")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/new_anna")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Equal("new_anna", (await db.Users.SingleAsync()).Username);
        Assert.Equal("new_anna", (await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!.Username);
    }

    [Fact]
    public async Task Public_search_is_paginated_literal_prefix_only_and_hides_private_account_data()
    {
        using var factory = new ApiFactory();
        await SeedAsync(factory);
        using var client = factory.CreateClient();
        var page = await client.GetFromJsonAsync<JsonElement>("/api/users?search=%40ANNA&pageSize=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal("anna", page.GetProperty("items")[0].GetProperty("username").GetString());
        var second = await client.GetFromJsonAsync<JsonElement>("/api/users?search=anna&pageSize=1&page=2");
        Assert.Equal("anna_style", second.GetProperty("items")[0].GetProperty("username").GetString());
        var exact = await client.GetFromJsonAsync<JsonElement>("/api/users/ANNA");
        Assert.False(exact.TryGetProperty("email", out _));
        Assert.False(page.GetProperty("items")[0].TryGetProperty("email", out _));
        var empty = await client.GetFromJsonAsync<JsonElement>("/api/users");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        var literal = await client.GetFromJsonAsync<JsonElement>("/api/users?search=anna_");
        Assert.Equal(1, literal.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users/anna_pending")).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647", "search=%25" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/users?" + query)).StatusCode);
    }

    [Fact]
    public async Task Public_profile_filters_owner_and_publication_but_preserves_private_outfit_members()
    {
        using var factory = new ApiFactory();
        var (owner, publicId, privateId, outfitId) = await SeedAsync(factory);
        using var client = await factory.OwnerClientAsync(owner);
        var items = await client.GetFromJsonAsync<JsonElement>("/api/users/anna/items");
        Assert.Equal(1, items.GetProperty("total").GetInt32());
        Assert.Equal(publicId, items.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal("anna", items.GetProperty("items")[0].GetProperty("ownerUsername").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/users/anna/items/{publicId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/anna/items/{privateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/anna_style/items/{publicId}")).StatusCode);
        var outfits = await client.GetFromJsonAsync<JsonElement>("/api/users/anna/outfits");
        Assert.Equal(1, outfits.GetProperty("total").GetInt32());
        var outfit = await client.GetFromJsonAsync<JsonElement>($"/api/users/anna/outfits/{outfitId}");
        Assert.Equal(privateId, outfit.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/anna_style/outfits/{outfitId}")).StatusCode);
        // Existing public listings and their address formats remain supported.
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/wardrobe");
        Assert.Equal("anna", catalog.GetProperty("items")[0].GetProperty("ownerUsername").GetString());
        var legacy = await client.GetFromJsonAsync<JsonElement>("/api/wardrobe/outfits");
        Assert.Equal(outfitId, legacy[0].GetProperty("id").GetGuid());
        Assert.Equal("anna", legacy[0].GetProperty("ownerUsername").GetString());
    }

    private static async Task<(Guid Owner, Guid Public, Guid Private, Guid Outfit)> SeedAsync(ApiFactory factory)
    {
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        var owner = new User(Guid.NewGuid(), "Анна", "anna");
        foreach (var user in new[] { owner, new User(Guid.NewGuid(), "Style", "anna_style"), new User(Guid.NewGuid(), "Pending", "anna_pending") })
        {
            db.Users.Add(user);
            db.Accounts.Add(new Account { UserId = user.Id, Email = user.Username + "@example.com", NormalizedEmail = user.Username.ToUpperInvariant() + "@EXAMPLE.COM",
                PasswordHash = "unused test hash", EmailConfirmed = user.Username != "anna_pending" });
        }
        var item = new WardrobeItem(Guid.NewGuid(), owner.Id, "Coat", ClothingCategory.Outerwear);
        item.Publish();
        var hidden = new WardrobeItem(Guid.NewGuid(), owner.Id, "Private shirt", ClothingCategory.Top);
        var outfit = new Outfit(Guid.NewGuid(), owner.Id, "Published outfit");
        outfit.AddItem(hidden); outfit.Publish();
        db.WardrobeItems.AddRange(item, hidden);
        db.Outfits.AddRange(outfit, new Outfit(Guid.NewGuid(), owner.Id, "Draft"));
        await db.SaveChangesAsync();
        return (owner.Id, item.Id, hidden.Id, outfit.Id);
    }
}
