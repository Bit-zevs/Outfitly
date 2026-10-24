using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Api;

public sealed class EmailConfirmationApiTests
{
    private const string Email = "confirm@example.com";
    private const string Password = "a confirmation password";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_confirmation_or_resend_save_preserves_original_token_and_never_sends(bool resend)
    {
        var failure = new FailSave();
        using var factory = new ApiFactory(testAuthentication: false, configureDatabase: o => o.AddInterceptors(failure));
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await Register(client);
        var original = factory.Mail.Emails[Email];
        await AllowResend(factory);
        failure.Enabled = true;
        var response = resend ? await Resend(client) : await client.PostAsJsonAsync("/api/auth/confirm-email", new { original.Token });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(original, factory.Mail.Emails[Email]);
        using (var read = factory.Services.CreateScope())
        {
            var db = read.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            Assert.False((await db.Accounts.SingleAsync()).EmailConfirmed);
            Assert.Empty(await db.LoginSessions.ToListAsync());
        }
        failure.Enabled = false;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/confirm-email", new { original.Token })).StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/confirm-email")]
    [InlineData("/api/auth/resend-confirmation")]
    public async Task New_commands_share_the_account_request_limit(string route)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await ApiFactory.SetCsrfAsync(client);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(route, new { email = Email, token = "invalid" })).StatusCode);
    }

    [Fact]
    public async Task Registration_requires_confirmation_and_token_can_be_used_only_once()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var registered = await Register(client);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.True((await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("confirmationEmailSent").GetBoolean());
        var token = factory.Mail.Emails[Email].Token;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            var account = await db.Accounts.SingleAsync();
            Assert.False(account.EmailConfirmed);
            Assert.NotEqual(token, account.ConfirmationTokenHash);
            Assert.Equal(64, account.ConfirmationTokenHash!.Length);
        }
        var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("email_not_confirmed", (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(blocked.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = "incorrect password" })).StatusCode);
        var confirmed = await client.PostAsJsonAsync("/api/auth/confirm-email", new { token });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.False(confirmed.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/confirm-email", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).StatusCode);
        using var read = factory.Services.CreateScope();
        var saved = read.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        var result = await saved.Accounts.SingleAsync();
        Assert.True(result.EmailConfirmed);
        Assert.Null(result.ConfirmationTokenHash);
        Assert.Null(result.ConfirmationExpiresAtUtc);
        Assert.Single(await saved.LoginSessions.ToListAsync());
    }

    [Fact]
    public async Task Smtp_failure_keeps_account_and_resend_replaces_token_after_cooldown()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        factory.Mail.Fail = true;
        var registered = await Register(client);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.False((await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("confirmationEmailSent").GetBoolean());
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Resend(client)).StatusCode);
        await AllowResend(factory);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Resend(client)).StatusCode);
        await AllowResend(factory);
        factory.Mail.Fail = false;
        Assert.Equal(HttpStatusCode.Accepted, (await Resend(client)).StatusCode);
        var oldToken = factory.Mail.Emails[Email].Token;
        await AllowResend(factory);
        Assert.Equal(HttpStatusCode.Accepted, (await Resend(client)).StatusCode);
        var newToken = factory.Mail.Emails[Email].Token;
        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/confirm-email", new { token = oldToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/confirm-email", new { token = newToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Resend(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = "missing@example.com" })).StatusCode);
        Assert.Equal(newToken, factory.Mail.Emails[Email].Token);
    }

    [Theory]
    [InlineData("/api/auth/confirm-email")]
    [InlineData("/api/auth/resend-confirmation")]
    public async Task New_commands_require_csrf(string route)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { email = Email, token = "invalid" })).StatusCode);
        Assert.Empty(factory.Mail.Emails);
    }

    [Fact]
    public async Task Expired_and_malformed_tokens_are_rejected_without_confirming_account()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await Register(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
            (await db.Accounts.SingleAsync()).ConfirmationExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        foreach (var token in new[] { factory.Mail.Emails[Email].Token, "invalid", new string('A', 43) })
        {
            var response = await client.PostAsJsonAsync("/api/auth/confirm-email", new { token });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_confirmation_token", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using var read = factory.Services.CreateScope();
        Assert.False((await read.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Accounts.SingleAsync()).EmailConfirmed);
    }

    private static async Task<HttpResponseMessage> Register(HttpClient client)
    {
        await ApiFactory.SetCsrfAsync(client);
        return await client.PostAsJsonAsync("/api/auth/register", new { email = Email, password = Password, displayName = "Confirm", username = "confirm" });
    }
    private static Task<HttpResponseMessage> Resend(HttpClient client) => client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = Email });
    private static async Task AllowResend(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        (await db.Accounts.SingleAsync()).ConfirmationIssuedAtUtc = DateTime.UtcNow.AddMinutes(-2);
        await db.SaveChangesAsync();
    }

    private sealed class FailSave : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new InvalidOperationException("Test save failure");
            return ValueTask.FromResult(result);
        }
    }
}
