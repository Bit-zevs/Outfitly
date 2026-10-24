using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Api;

public sealed class AccountSettingsApiTests
{
    private const string Email = "settings@example.com";
    private const string Password = "original settings password";
    private static async Task<AccountView> RegisterAndLogin(ApiFactory factory, HttpClient client)
    {
        await factory.InitializeDatabaseAsync();
        await ApiFactory.SetCsrfAsync(client);
        (await client.PostAsJsonAsync("/api/auth/register", new { email = Email, password = Password, displayName = "Original", username = "original" })).EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(client, Email);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });
        login.EnsureSuccessStatusCode();
        await ApiFactory.SetCsrfAsync(client);
        return (await login.Content.ReadFromJsonAsync<AccountView>())!;
    }

    [Fact]
    public async Task Profile_changes_atomically_and_preserves_the_current_session()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var client = factory.CreateClient();
        var original = await RegisterAndLogin(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            db.Users.Add(new Outfitly.Domain.User(Guid.NewGuid(), "Other", "occupied"));
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync("/api/auth/me/profile", new { displayName = "Changed", username = "occupied" })).StatusCode);
        Assert.Equal(original, await client.GetFromJsonAsync<AccountView>("/api/auth/me"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/auth/me/profile", new { displayName = "  ", username = "available" })).StatusCode);
        var changed = await client.PatchAsJsonAsync("/api/auth/me/profile", new { displayName = " Анна ", username = " @ANNA " });
        changed.EnsureSuccessStatusCode();
        var user = (await changed.Content.ReadFromJsonAsync<AccountView>())!;
        Assert.Equal("Анна", user.DisplayName); Assert.Equal("anna", user.Username);
        Assert.Equal(user, await client.GetFromJsonAsync<AccountView>("/api/auth/me"));
        using var read = factory.Services.CreateScope();
        Assert.Equal("Анна", (await read.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Users.SingleAsync(u => u.Id == original.Id)).DisplayName);
    }

    [Fact]
    public async Task Password_change_checks_current_password_then_revokes_all_cookies()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var client = factory.CreateClient();
        await RegisterAndLogin(factory, client);
        using var other = factory.CreateClient();
        await ApiFactory.SetCsrfAsync(other);
        (await other.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        var wrong = await client.PostAsJsonAsync("/api/auth/me/change-password", new { currentPassword = "wrong", newPassword = "new password" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("invalid_current_password", (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var changed = await client.PostAsJsonAsync("/api/auth/me/change-password", new { currentPassword = Password, newPassword = "new password" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Contains(changed.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("__Host-Outfitly.Auth=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = "new password" })).StatusCode);
    }

    [Fact]
    public async Task Email_change_keeps_old_login_until_explicit_confirmation_and_revokes_sessions()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var client = factory.CreateClient();
        await RegisterAndLogin(factory, client);
        var sent = await client.PostAsJsonAsync("/api/auth/me/email-change", new { email = " New@example.com ", currentPassword = Password });
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        Assert.True((await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("confirmationEmailSent").GetBoolean());
        var me = (await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!;
        Assert.Equal(Email, me.Email); Assert.Equal("New@example.com", me.PendingEmail);
        var publicUser = await client.GetFromJsonAsync<JsonElement>("/api/users/original");
        Assert.False(publicUser.TryGetProperty("pendingEmail", out _));
        Assert.False(publicUser.TryGetProperty("email", out _));
        using var other = factory.CreateClient();
        await ApiFactory.SetCsrfAsync(other);
        (await other.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        var mail = factory.Mail.Emails["New@example.com"];
        Assert.Equal(AccountEmailPurpose.EmailChange, mail.Purpose);
        var confirmed = await client.PostAsJsonAsync("/api/auth/confirm-email-change", new { mail.Token });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Contains(confirmed.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("__Host-Outfitly.Auth=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/confirm-email-change", new { mail.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = "new@EXAMPLE.COM", password = Password })).StatusCode);
        using var read = factory.Services.CreateScope();
        var account = await read.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Accounts.SingleAsync();
        Assert.Equal("New@example.com", account.Email); Assert.Null(account.PendingEmail); Assert.True(account.EmailConfirmed);
    }

    [Fact]
    public async Task Email_is_checked_again_at_confirmation_and_conflict_does_not_revoke_sessions()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var client = factory.CreateClient();
        await RegisterAndLogin(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/me/email-change", new { email = Email.ToUpperInvariant(), currentPassword = Password })).StatusCode);
        (await client.PostAsJsonAsync("/api/auth/me/email-change", new { email = "occupied@example.com", currentPassword = Password })).EnsureSuccessStatusCode();
        var token = factory.Mail.Emails["occupied@example.com"].Token;
        using (var scope = factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            await accounts.RegisterAsync("occupied@example.com", Password, "Other", "other");
            await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().SaveChangesAsync();
        }
        var conflict = await client.PostAsJsonAsync("/api/auth/confirm-email-change", new { token });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("email_taken", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(Email, (await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!.Email);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/me/email-change", new { email = "occupied@example.com", currentPassword = Password })).StatusCode);
    }

    [Fact]
    public async Task Confirmation_needs_csrf_and_preserves_another_accounts_cookie()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var owner = factory.CreateClient();
        await RegisterAndLogin(factory, owner);
        (await owner.PostAsJsonAsync("/api/auth/me/email-change", new { email = "new@example.com", currentPassword = Password })).EnsureSuccessStatusCode();
        var token = factory.Mail.Emails["new@example.com"].Token;
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/auth/confirm-email-change", new { token })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var registered = await accounts.RegisterAsync("other@example.com", Password, "Other", "other");
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            await db.SaveChangesAsync();
            await accounts.ConfirmEmailAsync(registered.Confirmation.Token);
            await db.SaveChangesAsync();
        }
        await ApiFactory.SetCsrfAsync(other);
        (await other.PostAsJsonAsync("/api/auth/login", new { email = "other@example.com", password = Password })).EnsureSuccessStatusCode();
        await ApiFactory.SetCsrfAsync(other);
        var result = await other.PostAsJsonAsync("/api/auth/confirm-email-change", new { token });
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.False(result.Headers.Contains("Set-Cookie"));
        Assert.Equal("other@example.com", (await other.GetFromJsonAsync<AccountView>("/api/auth/me"))!.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Failed_delivery_is_reported_and_cancellation_invalidates_the_saved_request()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var client = factory.CreateClient();
        await RegisterAndLogin(factory, client);
        factory.Mail.Fail = true;
        var response = await client.PostAsJsonAsync("/api/auth/me/email-change", new { email = "new@example.com", currentPassword = Password });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("confirmationEmailSent").GetBoolean());
        Assert.Equal("new@example.com", (await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!.PendingEmail);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/me/email-change", new { email = "new@example.com", currentPassword = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/auth/me/email-change")).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<AccountView>("/api/auth/me"))!.PendingEmail);
        using var read = factory.Services.CreateScope();
        Assert.Null((await read.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Accounts.SingleAsync()).EmailChangeTokenHash);
    }

    [Theory]
    [InlineData("PATCH", "/api/auth/me/profile")]
    [InlineData("POST", "/api/auth/me/change-password")]
    [InlineData("POST", "/api/auth/me/email-change")]
    [InlineData("DELETE", "/api/auth/me/email-change")]
    public async Task Settings_commands_require_authentication_and_csrf(string method, string path)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        HttpRequestMessage Request() => new(new HttpMethod(method), path)
        { Content = JsonContent.Create(new { displayName = "Changed", username = "changed", currentPassword = Password, newPassword = "new password", email = "new@example.com" }) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request())).StatusCode);
        await RegisterAndLogin(factory, client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Request())).StatusCode);
        using var read = factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Equal("Original", (await db.Users.SingleAsync()).DisplayName);
        Assert.Null((await db.Accounts.SingleAsync()).PendingEmail);
        Assert.All(await db.LoginSessions.ToArrayAsync(), session => Assert.False(session.IsRevoked));
    }
}
