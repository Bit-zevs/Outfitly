using Microsoft.EntityFrameworkCore;
using Outfitly.Domain;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Npgsql;

namespace Outfitly.Infrastructure.Persistence;

public sealed class OutfitlyDbContext(DbContextOptions<OutfitlyDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<WardrobeItem> WardrobeItems => Set<WardrobeItem>();
    public DbSet<Outfit> Outfits => Set<Outfit>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try { return base.SaveChanges(acceptAllChangesOnSuccess); }
        catch (DbUpdateConcurrencyException exception) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception)) { throw new AccountConflictException(exception); }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch (DbUpdateConcurrencyException exception) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception)) { throw new AccountConflictException(exception); }
    }

    private static bool IsDuplicateEmail(DbUpdateException exception) => exception.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Accounts_NormalizedEmail"
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OutfitlyDbContext).Assembly);
    }
}
