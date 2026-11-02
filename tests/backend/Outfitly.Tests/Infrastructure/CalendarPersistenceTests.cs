using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
namespace Outfitly.Tests.Infrastructure;
public sealed class CalendarPersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sqlite_assignment_races_use_outfit_revision(bool assignmentWins)
    {
        await using var database = await TestDatabase.CreateAsync(); await Race(database.Open, assignmentWins);
    }
    [PostgreSqlFact]
    public async Task PostgreSql_migration_and_both_deletion_races()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await Race(database.Open, true); await Race(database.Open, false); await SameAssignment(database.Open);
        await using var seed = database.Open();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var id = Guid.NewGuid();
        seed.Users.AddRange(new User(owner, "Owner"), new User(other, "Other"));
        seed.Outfits.Add(new Outfit(id, owner, "Empty")); await seed.SaveChangesAsync();
        seed.Add(new OutfitAssignment(other, new DateOnly(2028, 2, 29), id));
        await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => seed.SaveChangesAsync());
    }
    private static OutfitCalendar Calendar(OutfitlyDbContext context)
    {
        var repo = new EfWardrobeRepository(context);
        return new(new EfOutfitCalendarStore(context), repo, new WardrobeService(repo));
    }
    private static async Task Race(Func<OutfitlyDbContext> open, bool assignmentWins)
    {
        var owner = Guid.NewGuid(); var id = Guid.NewGuid(); var date = new DateOnly(2028, 2, 29);
        await using (var seed = open()) { seed.Users.Add(new User(owner, "Owner")); seed.Outfits.Add(new Outfit(id, owner, "Outfit")); await seed.SaveChangesAsync(); }
        await using var assign = open(); await using var delete = open();
        Calendar(assign).Set(owner, date, id, true);
        new WardrobeService(new EfWardrobeRepository(delete)).DeleteOutfit(owner, id);
        if (assignmentWins)
        {
            await assign.SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => delete.SaveChangesAsync());
            await using var retry = open(); new WardrobeService(new EfWardrobeRepository(retry)).DeleteOutfit(owner, id); await retry.SaveChangesAsync();
        }
        else
        {
            await delete.SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => assign.SaveChangesAsync());
        }
        await using var read = open(); Assert.Empty(Calendar(read).ReadMonth(owner, date));
        Assert.False(await read.Set<OutfitAssignment>().AnyAsync(x => x.OutfitId == id));
    }
    [Fact]
    public async Task Concurrent_same_assignment_requires_fresh_retry()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SameAssignment(database.Open);
    }
    private static async Task SameAssignment(Func<OutfitlyDbContext> open)
    {
        var owner = Guid.NewGuid(); var id = Guid.NewGuid(); var date = new DateOnly(2028, 2, 29);
        await using (var seed = open()) { seed.Users.Add(new User(owner, "Owner")); seed.Outfits.Add(new Outfit(id, owner, "Empty")); await seed.SaveChangesAsync(); }
        await using var first = open(); await using var second = open();
        Calendar(first).Set(owner, date, id, true); Calendar(second).Set(owner, date, id, true);
        await first.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => second.SaveChangesAsync());
        await using (var retry = open()) { Calendar(retry).Set(owner, date, id, true); await retry.SaveChangesAsync(); }
        await using var read = open(); Assert.Single(Calendar(read).ReadMonth(owner, date));
    }
}
