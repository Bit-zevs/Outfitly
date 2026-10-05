using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class OutfitReadTests
{
    [Theory]
    [InlineData("public", 2)]
    [InlineData("personal", 2)]
    [InlineData("single", 2)]
    [InlineData("shared", 3)]
    public async Task Outfit_views_load_items_once_per_operation_and_preserve_membership(string view, int maximumReads)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var counter = new SelectCounter();
        var options = new DbContextOptionsBuilder<OutfitlyDbContext>().UseSqlite(connection).AddInterceptors(counter).Options;
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        Guid firstId = default;
        string token = "";
        Guid[] itemIds;
        await using (var seed = new OutfitlyDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Users.AddRange(new User(owner, "Owner"), new User(otherOwner, "Other"));
            var service = OutfitConcurrencyScenarios.Service(seed);
            itemIds = Enumerable.Range(0, 3).Select(i => service.CreateItem(owner, $"Item {i}", ClothingCategory.Top).Id).ToArray();
            service.CreateItem(otherOwner, "Unrelated private item", ClothingCategory.Dress);
            for (var i = 0; i < 4; i++)
            {
                var outfit = service.CreateOutfit(owner, $"Outfit {i}");
                foreach (var itemId in itemIds) service.AddItemToOutfit(owner, outfit.Id, itemId);
                service.SetPublication(owner, ShareTargetType.Outfit, outfit.Id, true);
                if (i == 0)
                {
                    firstId = outfit.Id;
                    token = service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id).Token;
                }
            }
            await seed.SaveChangesAsync();
        }
        counter.Reads = 0;
        await using var read = new OutfitlyDbContext(options);
        var reader = OutfitConcurrencyScenarios.Service(read);
        IReadOnlyCollection<OutfitView> result = view switch
        {
            "public" => reader.GetPublicOutfits(),
            "personal" => reader.GetMyOutfits(owner),
            "single" => [reader.GetOutfit(owner, firstId)],
            "shared" => [reader.GetSharedOutfit(token)!],
            _ => throw new ArgumentException("Unknown view.")
        };
        Assert.Equal(view is "public" or "personal" ? 4 : 1, result.Count);
        foreach (var outfit in result)
        {
            Assert.Equal(owner, outfit.OwnerId);
            Assert.True(outfit.IsPublic);
            Assert.Equal(itemIds.Order(), outfit.Items.Select(item => item.Id).Order());
            Assert.All(outfit.Items, item => Assert.False(item.IsPublic));
        }
        Assert.InRange(counter.Reads, 1, maximumReads);
    }

    private sealed class SelectCounter : DbCommandInterceptor
    {
        internal int Reads { get; set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Reads++;
            return result;
        }
    }
}
