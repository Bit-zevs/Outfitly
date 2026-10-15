using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Api;

public sealed class PasswordResetApiTests
{
    private const string Email = "reset@example.com";
    private const string Password = "original password";

    private static async Task Register(HttpClient client, ApiFactory factory)
    {
        await ApiFactory.SetCsrfAsync(client);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new { email = Email, password = Password, displayName = "Reset" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/confirm-email", new { factory.Mail.Emails[Email].Token })).StatusCode);
    }

    [Fact]
    public async Task Reset_changes_password_once_and_revokes_existing_sessions()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await Register(client, factory);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = Email })).StatusCode);
        var email = factory.Mail.Emails[Email];
        Assert.Equal(AccountEmailPurpose.PasswordReset, email.Purpose);
        using (var scope = factory.Services.CreateScope())
        {
            var account = await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Accounts.SingleAsync();
            Assert.NotEqual(email.Token, account.PasswordResetTokenHash);
            Assert.Equal(64, account.PasswordResetTokenHash!.Length);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/reset-password", new { email.Token, password = "replacement password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await ApiFactory.SetCsrfAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", new { email.Token, password = "another password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = "replacement password" })).StatusCode);
        using var read = factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.Null((await db.Accounts.SingleAsync()).PasswordResetTokenHash);
        Assert.Single(await db.LoginSessions.Where(s => s.IsRevoked).ToListAsync());
    }

    [Fact]
    public async Task Unknown_email_cooldown_and_delivery_failure_have_same_response()
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await Register(client, factory);
        factory.Mail.Fail = true;
        string? expected = null;
        foreach (var address in new[] { "missing@example.com", Email, Email })
        {
            var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = address });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            expected ??= body;
            Assert.Equal(expected, body);
        }
    }

    [Theory]
    [InlineData("/api/auth/forgot-password")]
    [InlineData("/api/auth/reset-password")]
    public async Task Reset_commands_require_csrf(string route)
    {
        using var factory = new ApiFactory(testAuthentication: false);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { email = Email, token = "invalid", password = Password })).StatusCode);
        Assert.Empty(factory.Mail.Emails);
    }
}
