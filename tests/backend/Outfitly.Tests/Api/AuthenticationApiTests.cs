using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Api;

public sealed class AuthenticationApiTests
{
    private const string Password = "a long password with spaces";

    [Fact]
    public async Task Registration_persists_a_user_without_logging_in_and_email_is_case_insensitive()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var response = await Register(client, "  Alice@example.com  ");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(HasAuthCookie(response));
        var user = await response.Content.ReadFromJsonAsync<AccountView>();
        Assert.NotNull(user);
        Assert.Equal("Alice@example.com", user.Email);
        Assert.Equal("Alice", user.DisplayName);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Register(client, "alice@EXAMPLE.COM")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, "ALICE@example.com")).StatusCode);
        Assert.Equal(user, await client.GetFromJsonAsync<AccountView>("/api/auth/me"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Equal(user.Id, (await db.Users.SingleAsync()).Id);
        var account = await db.Accounts.SingleAsync();
        Assert.NotEqual(Password, account.PasswordHash);
        Assert.Equal(PasswordCheck.Success,
            scope.ServiceProvider.GetRequiredService<IAccountPasswordHasher>().Verify(account.PasswordHash, Password));
        Assert.Single(await db.LoginSessions.ToListAsync());
    }

    [Fact]
    public async Task Real_accounts_can_use_their_wardrobe_but_cannot_access_another_owners_resources()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var alice = factory.CreateClient();
        using var bob = factory.CreateClient();
        (await Register(alice, "alice@example.com")).EnsureSuccessStatusCode();
        (await Register(bob, "bob@example.com")).EnsureSuccessStatusCode();
        var owner = (await (await Login(alice, "alice@example.com")).Content.ReadFromJsonAsync<AccountView>())!;
        (await Login(bob, "bob@example.com")).EnsureSuccessStatusCode();
        await ApiFactory.SetCsrfAsync(alice);
        await ApiFactory.SetCsrfAsync(bob);
        alice.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
        var created = await alice.PostAsJsonAsync("/api/items", new
        {
            name = "Coat", category = "Outerwear", ownerId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(owner.Id, item.GetProperty("ownerId").GetGuid());
        var route = $"/api/items/{item.GetProperty("id").GetGuid()}";
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync(route)).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement>("/api/items")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task Cookies_are_secure_logout_revokes_only_current_session_and_replay_is_rejected()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var browser = factory.CreateClient();
        (await Register(browser, "alice@example.com")).EnsureSuccessStatusCode();
        var login = await Login(browser, "alice@example.com");
        var header = AuthCookieHeader(login);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", header, StringComparison.OrdinalIgnoreCase);
        var authOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var authCookie = header.Split(';')[0];
        var ticket = authOptions.TicketDataFormat.Unprotect(authCookie[(authCookie.IndexOf('=') + 1)..])!;
        Assert.Equal(TimeSpan.FromHours(8), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        using var secondBrowser = factory.CreateClient();
        (await Login(secondBrowser, "alice@example.com")).EnsureSuccessStatusCode();
        await ApiFactory.SetCsrfAsync(browser);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/items")).StatusCode);
        using var replay = ReplayClient(factory, header.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondBrowser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Switching_accounts_revokes_the_old_session_and_requires_a_new_csrf_token()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var browser = factory.CreateClient();
        using var other = factory.CreateClient();
        (await Register(browser, "alice@example.com")).EnsureSuccessStatusCode();
        (await Register(other, "bob@example.com")).EnsureSuccessStatusCode();
        var aliceLogin = await Login(browser, "alice@example.com");
        var aliceCookie = AuthCookieHeader(aliceLogin);
        await ApiFactory.SetCsrfAsync(browser);

        var switched = await browser.PostAsJsonAsync("/api/auth/login",
            new { email = "bob@example.com", password = Password });

        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var bob = await switched.Content.ReadFromJsonAsync<AccountView>();
        Assert.Equal("bob@example.com", bob?.Email);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await browser.PostAsJsonAsync("/api/outfits", new { name = "Stale CSRF" })).StatusCode);
        await ApiFactory.SetCsrfAsync(browser);
        Assert.Equal(HttpStatusCode.Created,
            (await browser.PostAsJsonAsync("/api/outfits", new { name = "Bob's outfit" })).StatusCode);
        using var replay = ReplayClient(factory, aliceCookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/login")]
    public async Task Anonymous_auth_commands_require_csrf(string route)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var data = new { email = "alice@example.com", password = Password, displayName = "Alice" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, data)).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "forged");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, data)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task Csrf_must_be_refreshed_after_login_and_protects_wardrobe_and_logout()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        (await Register(client, "alice@example.com")).EnsureSuccessStatusCode();
        (await Login(client, "alice@example.com")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/outfits", new { name = "Evening" })).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/outfits", new { name = "Evening" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.True(csrf.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_have_the_same_response_and_do_not_create_sessions()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        (await Register(client, "alice@example.com")).EnsureSuccessStatusCode();
        var wrong = await Login(client, "alice@example.com", "a different password");
        var unknown = await Login(client, "missing@example.com", "a different password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        var wrongBody = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(wrongBody.GetProperty("title").GetString(), unknownBody.GetProperty("title").GetString());
        Assert.Equal(wrongBody.GetProperty("status").GetInt32(), unknownBody.GetProperty("status").GetInt32());
        Assert.False(wrongBody.TryGetProperty("detail", out _));
        Assert.False(unknownBody.TryGetProperty("detail", out _));
        Assert.False(HasAuthCookie(wrong));
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().LoginSessions.ToListAsync());
    }

    [Theory]
    [InlineData("database-expired")]
    [InlineData("cookie-expired")]
    [InlineData("account-deleted")]
    [InlineData("forged")]
    public async Task Invalid_sessions_are_rejected_without_redirects(string reason)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        (await Register(client, "alice@example.com")).EnsureSuccessStatusCode();
        var login = await Login(client, "alice@example.com");
        var cookie = AuthCookieHeader(login).Split(';')[0];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            if (reason == "database-expired")
            {
                var session = await db.LoginSessions.SingleAsync();
                db.Entry(session).Property(s => s.ExpiresAtUtc).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }
            if (reason == "account-deleted") await db.Accounts.ExecuteDeleteAsync();
            if (reason == "cookie-expired")
            {
                var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                    .Get(CookieAuthenticationDefaults.AuthenticationScheme);
                var ticket = options.TicketDataFormat.Unprotect(cookie[(cookie.IndexOf('=') + 1)..])!;
                ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                cookie = $"__Host-Outfitly.Auth={options.TicketDataFormat.Protect(ticket)}";
            }
        }
        if (reason == "forged") cookie = "__Host-Outfitly.Auth=not-a-protected-ticket";
        using var replay = ReplayClient(factory, cookie);
        var rejected = await replay.GetAsync("/api/items");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Null(rejected.Headers.Location);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("invalid", "long enough password", "Alice")]
    [InlineData("alice@example.com", "too short", "Alice")]
    [InlineData("alice@example.com", "long enough password", "  ")]
    public async Task Invalid_registration_does_not_create_partial_data(string email, string password, string name)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await ApiFactory.SetCsrfAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName = name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Accounts.ToListAsync());
    }

    [Fact]
    public async Task Registration_accepts_the_email_length_limit_and_rejects_one_character_over()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var maximumEmail = $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 61)}";
        Assert.Equal(254, maximumEmail.Length);
        var tooLongEmail = maximumEmail + "e";
        Assert.Equal(255, tooLongEmail.Length);

        Assert.Equal(HttpStatusCode.Created, (await Register(client, maximumEmail)).StatusCode);
        var rejected = await Register(client, tooLongEmail);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var scope = factory.Services.CreateScope();
        var accounts = await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Accounts.ToListAsync();
        Assert.Equal(maximumEmail, Assert.Single(accounts).Email);
    }

    [Fact]
    public async Task A_user_from_before_authentication_cannot_log_in_without_credentials()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            db.Users.Add(new User(Guid.NewGuid(), "Legacy user"));
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();

        var response = await Login(client, "legacy@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(HasAuthCookie(response));
        using var read = factory.Services.CreateScope();
        var persisted = read.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Equal("Legacy user", (await persisted.Users.SingleAsync()).DisplayName);
        Assert.Empty(await persisted.Accounts.ToListAsync());
    }

    [Fact]
    public async Task Registration_and_login_share_the_same_rate_limit()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await ApiFactory.SetCsrfAsync(client);
        for (var i = 0; i < 10; i++)
        {
            var route = i % 2 == 0 ? "register" : "login";
            var response = await client.PostAsJsonAsync($"/api/auth/{route}",
                new { email = "unknown@example.com", password = "wrong", displayName = "Alice" });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        var limited = await Login(client, "unknown@example.com");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_save_rolls_back_and_never_issues_auth_cookie(bool login)
    {
        var failure = new FailNextSave();
        using var factory = new ApiFactory(testAuthentication: false, configureDatabase: o => o.AddInterceptors(failure));
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        if (login) (await Register(client, "alice@example.com")).EnsureSuccessStatusCode();
        failure.Enabled = true;
        var response = login ? await Login(client, "alice@example.com") : await Register(client, "alice@example.com");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(HasAuthCookie(response));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Equal(login ? 1 : 0, await db.Users.CountAsync());
        Assert.Equal(login ? 1 : 0, await db.Accounts.CountAsync());
        Assert.Empty(await db.LoginSessions.ToListAsync());
    }

    private static async Task<HttpResponseMessage> Register(HttpClient client, string email)
    {
        await ApiFactory.SetCsrfAsync(client);
        return await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, displayName = " Alice " });
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client, string email, string password = Password)
    {
        await ApiFactory.SetCsrfAsync(client);
        return await client.PostAsJsonAsync("/api/auth/login", new { email, password });
    }

    private static bool HasAuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-Outfitly.Auth="));

    private static string AuthCookieHeader(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Outfitly.Auth="));
    }

    private static HttpClient ReplayClient(ApiFactory factory, string cookie)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
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
                // A real constraint failure exercises rollback of the full SaveChanges transaction.
                ((OutfitlyDbContext)eventData.Context!).WardrobeItems.Add(
                    new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "Missing owner", ClothingCategory.Top));
            }
            return ValueTask.FromResult(result);
        }
    }
}
