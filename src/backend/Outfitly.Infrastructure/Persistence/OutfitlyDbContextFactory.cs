using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Outfitly.Infrastructure.Persistence;

public sealed class OutfitlyDbContextFactory : IDesignTimeDbContextFactory<OutfitlyDbContext>
{
    public OutfitlyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("OUTFITLY_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set OUTFITLY_CONNECTION_STRING before running EF tools.");

        return new OutfitlyDbContext(new DbContextOptionsBuilder<OutfitlyDbContext>()
            .UseNpgsql(connectionString).Options);
    }
}
