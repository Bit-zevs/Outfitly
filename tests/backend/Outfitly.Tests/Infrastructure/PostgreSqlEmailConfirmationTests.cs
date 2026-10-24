using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlEmailConfirmationTests
{
    [PostgreSqlFact]
    public async Task Migration_confirms_existing_accounts_but_new_accounts_default_to_unconfirmed()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var existing = Guid.NewGuid();
        var added = Guid.NewGuid();
        await using (var migrate = database.Open())
        {
            var migrator = migrate.GetService<IMigrator>();
            await migrator.MigrateAsync("20261011105608_AccountsAndSessions");
            await migrate.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\", \"DisplayName\") VALUES ({existing}, 'Legacy')");
            await migrate.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"UserId\", \"Email\", \"NormalizedEmail\", \"PasswordHash\") VALUES ({existing}, 'old@example.com', 'OLD@EXAMPLE.COM', 'unused')");
            await migrator.MigrateAsync();
            await migrate.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\", \"DisplayName\", \"Username\") VALUES ({added}, 'New', 'new_user')");
            await migrate.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"UserId\", \"Email\", \"NormalizedEmail\", \"PasswordHash\") VALUES ({added}, 'new@example.com', 'NEW@EXAMPLE.COM', 'unused')");
        }
        await using var read = database.Open();
        Assert.True((await read.Accounts.SingleAsync(a => a.UserId == existing)).EmailConfirmed);
        Assert.False((await read.Accounts.SingleAsync(a => a.UserId == added)).EmailConfirmed);
        Assert.False(read.Database.HasPendingModelChanges());
        Assert.Empty(await read.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSQL_rejects_stale_confirmation_and_resend_commands()
    {
        foreach (var firstResend in new[] { false, true })
        foreach (var secondResend in new[] { false, true })
        {
            await using var database = await PostgreSqlDatabase.CreateAsync();
            await EmailConfirmationPersistenceTests.VerifyRace(database.Open, firstResend, secondResend);
        }
    }
}
