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
        try { PrepareItemRevisions(); return base.SaveChanges(acceptAllChangesOnSuccess); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "PostInteractions" }) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation, TableName: "UserFollows" }) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "Favorites" or "EditorDrafts" or "CopyReceipts" }) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateConcurrencyException exception) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception)) { throw new AccountConflictException(exception); }
        catch (DbUpdateException exception) when (IsDuplicateUsername(exception)) { throw new UsernameConflictException(exception); }
        catch (DbUpdateException exception) when (HasWorkspaceAddition(exception))
        {
            if (WorkspaceCollision(exception)) throw new WardrobeConcurrencyException(exception);
            throw;
        }
        catch (DbUpdateException exception) when (HasAddedInteraction(exception))
        {
            using var verification = new OutfitlyDbContext(options);
            if (InteractionTargets(exception).Any(target => target.Outfit
                ? !verification.Outfits.AsNoTracking().Any(x => x.Id == target.Id)
                : !verification.WardrobeItems.AsNoTracking().Any(x => x.Id == target.Id)))
                throw new WardrobeConcurrencyException(exception);
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try { PrepareItemRevisions(); return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "PostInteractions" }) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation, TableName: "UserFollows" }) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "Favorites" or "EditorDrafts" or "CopyReceipts" }) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateConcurrencyException exception) { throw new WardrobeConcurrencyException(exception); }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception)) { throw new AccountConflictException(exception); }
        catch (DbUpdateException exception) when (IsDuplicateUsername(exception)) { throw new UsernameConflictException(exception); }
        catch (DbUpdateException exception) when (HasWorkspaceAddition(exception))
        {
            if (WorkspaceCollision(exception)) throw new WardrobeConcurrencyException(exception);
            throw;
        }
        catch (DbUpdateException exception) when (HasAddedInteraction(exception))
        {
            // The failed context is never queried or saved again. Check only the target's
            // existence through a clean context after EF has rolled back the failed write.
            await using var verification = new OutfitlyDbContext(options);
            foreach (var target in InteractionTargets(exception))
            {
                var exists = target.Outfit
                    ? await verification.Outfits.AsNoTracking().AnyAsync(x => x.Id == target.Id, cancellationToken)
                    : await verification.WardrobeItems.AsNoTracking().AnyAsync(x => x.Id == target.Id, cancellationToken);
                if (!exists) throw new WardrobeConcurrencyException(exception);
            }
            throw;
        }
    }

    private void PrepareItemRevisions()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<WardrobeItem>().Where(entry => entry.State == EntityState.Modified))
            entry.Property<Guid>("Revision").CurrentValue = Guid.NewGuid();
    }

    private static bool HasWorkspaceAddition(DbUpdateException exception) => exception.Entries.Any(entry =>
        entry.State == EntityState.Added && entry.Entity is Favorite or EditorDraft or CopyReceipt);

    private bool WorkspaceCollision(DbUpdateException exception)
    {
        // Verify expected duplicate keys after rollback without querying the failed context.
        using var clean = new OutfitlyDbContext(options);
        return exception.Entries.Where(entry => entry.State == EntityState.Added).Any(entry => entry.Entity switch
        {
            Favorite f => clean.Set<Favorite>().Any(x => x.UserId == f.UserId && x.Kind == f.Kind && x.TargetId == f.TargetId),
            EditorDraft d => clean.Set<EditorDraft>().Any(x => x.Id == d.Id || x.UserId == d.UserId && x.ActiveSlot == d.ActiveSlot && d.ActiveSlot != null),
            CopyReceipt r => clean.Set<CopyReceipt>().Any(x => x.UserId == r.UserId && x.OperationId == r.OperationId),
            _ => false
        });
    }

    private static bool HasAddedInteraction(DbUpdateException exception) =>
        exception.Entries.Any(entry => entry.State == EntityState.Added && entry.Entity is PostInteraction);

    private static IEnumerable<(Guid Id, bool Outfit)> InteractionTargets(DbUpdateException exception) =>
        exception.Entries.Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity)
            .OfType<PostInteraction>().Select(interaction => interaction.OutfitId is { } outfitId
                ? (outfitId, true) : (interaction.ItemId!.Value, false)).Distinct();

    private static bool IsDuplicateUsername(DbUpdateException exception) => exception.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_Username"
    };

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
