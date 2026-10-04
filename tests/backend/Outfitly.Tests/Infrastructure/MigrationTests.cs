using Microsoft.EntityFrameworkCore;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class MigrationTests
{
    [Fact]
    public void The_committed_migration_covers_the_current_PostgreSQL_model()
    {
        var options = new DbContextOptionsBuilder<OutfitlyDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options;
        using var context = new OutfitlyDbContext(options);

        var hasUnmigratedChanges = context.Database.HasPendingModelChanges();

        Assert.False(hasUnmigratedChanges, "Update the PostgreSQL migration when changing the persistence model.");
        Assert.NotEmpty(context.Database.GetMigrations());
    }
}
