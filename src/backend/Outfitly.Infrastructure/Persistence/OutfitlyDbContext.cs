using Microsoft.EntityFrameworkCore;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence;

public sealed class OutfitlyDbContext(DbContextOptions<OutfitlyDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<WardrobeItem> WardrobeItems => Set<WardrobeItem>();
    public DbSet<Outfit> Outfits => Set<Outfit>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OutfitlyDbContext).Assembly);
    }
}
