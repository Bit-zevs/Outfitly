using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
namespace Outfitly.Tests.Infrastructure;
public sealed class SupportPersistenceTests
{
    [Fact]
    public async Task Sqlite_conflicting_reply_and_close_are_atomic() { await using var db = await TestDatabase.CreateAsync(); await Verify(db.Open); }
    [PostgreSqlFact]
    public async Task PostgreSql_migration_and_support_conflicts() { await using var db = await PostgreSqlDatabase.CreateAsync(); await Verify(db.Open); }
    private static async Task Verify(Func<OutfitlyDbContext> open)
    {
        var owner = Guid.NewGuid(); Guid id;
        await using (var seed = open())
        {
            seed.Users.Add(new User(owner, "Owner"));
            var ticket = new SupportTicket(owner, "problem", "Problem", DateTime.UtcNow); ticket.Reply(owner, false, "First", [], DateTime.UtcNow); id = ticket.Id;
            seed.Add(ticket); await seed.SaveChangesAsync();
        }
        await using var reply = open(); await using var close = open();
        var first = await new EfSupportStore(reply).FindAsync(id, default); var second = await new EfSupportStore(close).FindAsync(id, default);
        first!.Reply(owner, false, "Late", [], DateTime.UtcNow); second!.SetStatus("closed", DateTime.UtcNow);
        await close.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => reply.SaveChangesAsync());
        await using var fresh = open(); var saved = await new EfSupportStore(fresh).FindAsync(id, default);
        Assert.Equal("closed", saved!.Status); Assert.Single(saved.Messages);
        await using var status1 = open(); await using var status2 = open();
        var a = await new EfSupportStore(status1).FindAsync(id, default); var b = await new EfSupportStore(status2).FindAsync(id, default);
        a!.SetStatus("inProgress", DateTime.UtcNow); b!.SetStatus("closed", DateTime.UtcNow);
        await status1.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => status2.SaveChangesAsync());
    }
}
