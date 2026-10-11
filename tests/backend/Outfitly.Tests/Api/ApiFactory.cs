using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Api;

internal sealed class ApiFactory(bool testAuthentication = true,
    Action<DbContextOptionsBuilder>? configureDatabase = null) : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Outfitly", "Host=localhost;Database=unused;Username=unused");
        connection.Open();
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<OutfitlyDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OutfitlyDbContext>>();
            services.AddDbContext<OutfitlyDbContext>(options =>
            {
                options.UseSqlite(connection);
                configureDatabase?.Invoke(options);
            });
            if (testAuthentication)
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        });
    }

    internal async Task SeedOwnerAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Users.Add(new User(id, "Test owner"));
        await context.SaveChangesAsync();
    }

    internal new HttpClient CreateClient() => base.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    internal async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().Database.EnsureCreatedAsync();
    }

    internal async Task<HttpClient> OwnerClientAsync(Guid id)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", id.ToString());
        await SetCsrfAsync(client);
        return client;
    }

    internal static async Task SetCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

// Exists only in the test assembly. Production never trusts X-Test-User.
internal sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrEmpty(id)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
    }
}
