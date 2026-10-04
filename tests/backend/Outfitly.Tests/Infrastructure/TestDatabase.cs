using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<OutfitlyDbContext> options;

    private TestDatabase(SqliteConnection connection)
    {
        this.connection = connection;
        options = new DbContextOptionsBuilder<OutfitlyDbContext>().UseSqlite(connection).Options;
    }

    internal static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var database = new TestDatabase(connection);
        await using var context = database.Open();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    internal OutfitlyDbContext Open() => new(options);
    public ValueTask DisposeAsync() => connection.DisposeAsync();
}

internal sealed record WardrobeData(Guid Owner, Guid ItemId, Guid OutfitId, string ItemToken, string OutfitToken)
{
    internal static async Task<WardrobeData> SeedAsync(OutfitlyDbContext context)
    {
        var owner = Guid.NewGuid();
        context.Users.Add(new User(owner, "Alice"));
        var service = new WardrobeService(new EfWardrobeRepository(context));
        var item = service.CreateItem(owner, "Shirt", ClothingCategory.Top, "White", "M", "Brand",
            "Cotton", "https://example.com/shirt.jpg");
        var outfit = service.CreateOutfit(owner, "Daily", "Summer outfit");
        service.AddItemToOutfit(owner, outfit.Id, item.Id);
        outfit.Publish();
        var itemLink = service.CreateShareLink(owner, ShareTargetType.WardrobeItem, item.Id);
        var outfitLink = service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id);
        await context.SaveChangesAsync();
        return new WardrobeData(owner, item.Id, outfit.Id, itemLink.Token, outfitLink.Token);
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OUTFITLY_TEST_CONNECTION_STRING")))
            Skip = "Set OUTFITLY_TEST_CONNECTION_STRING to a dedicated PostgreSQL test database.";
    }
}

internal sealed class PostgreSqlDatabase : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly string schema = $"outfitly_test_{Guid.NewGuid():N}";
    private readonly DbContextOptions<OutfitlyDbContext> options;

    private PostgreSqlDatabase(string connectionString)
    {
        this.connectionString = connectionString;
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
        options = new DbContextOptionsBuilder<OutfitlyDbContext>().UseNpgsql(builder.ConnectionString).Options;
    }

    internal static async Task<PostgreSqlDatabase> CreateAsync()
    {
        var database = new PostgreSqlDatabase(Environment.GetEnvironmentVariable("OUTFITLY_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("PostgreSQL test connection is missing."));
        await database.ExecuteAdminAsync($"CREATE SCHEMA \"{database.schema}\"");
        try
        {
            await using var context = database.Open();
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    internal OutfitlyDbContext Open() => new(options);

    public async ValueTask DisposeAsync() => await ExecuteAdminAsync($"DROP SCHEMA \"{schema}\" CASCADE");

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
