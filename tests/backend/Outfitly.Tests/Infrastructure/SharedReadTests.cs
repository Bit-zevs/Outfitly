using Microsoft.EntityFrameworkCore.Diagnostics;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class SharedReadTests
{
    [Fact]
    public async Task Shared_reads_only_materialize_the_selected_resources()
    {
        await using var database = await TestDatabase.CreateAsync();
        await CheckReads(database.Open);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_shared_reads_only_materialize_the_selected_resources()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await CheckReads(database.Open);
    }

    private static async Task CheckReads(Func<IInterceptor[], OutfitlyDbContext> open)
    {
        WardrobeData data;
        string revoked;
        Guid unrelatedId;
        await using (var write = open([]))
        {
            data = await WardrobeData.SeedAsync(write);
            (await write.WardrobeItems.FindAsync(data.ItemId))!.SetUploadedPhoto("selected-photo");
            unrelatedId = Guid.NewGuid();
            for (var i = 0; i < 500; i++)
            {
                var item = new WardrobeItem(i == 0 ? unrelatedId : Guid.NewGuid(), data.Owner,
                    "Unrelated", ClothingCategory.Top);
                var outfit = new Outfit(Guid.NewGuid(), data.Owner, "Unrelated outfit");
                outfit.AddItem(item);
                write.AddRange(item, outfit, new ShareLink(Guid.NewGuid(), ShareTargetType.WardrobeItem, item.Id));
            }
            var inactive = new ShareLink(Guid.NewGuid(), ShareTargetType.WardrobeItem, data.ItemId);
            inactive.Deactivate();
            revoked = inactive.Token;
            write.Add(inactive);
            await write.SaveChangesAsync();
        }

        var materialized = new MaterializedEntities();
        await using var read = open([materialized]);
        var service = new WardrobeService(new EfWardrobeRepository(read));
        foreach (var token in new[] { "missing", "' OR 1=1 --", revoked })
        {
            Assert.Null(service.GetSharedItem(token));
            Assert.Null(service.GetSharedOutfit(token));
            Assert.Null(service.GetSharedPhoto(token));
            Assert.Null(service.GetSharedPhoto(token, data.ItemId));
        }
        Assert.Empty(materialized.Entities);
        Assert.Null(service.GetSharedItem(data.OutfitToken));
        Assert.Null(service.GetSharedOutfit(data.ItemToken));
        Assert.Empty(materialized.Entities);

        Assert.Equal(data.ItemId, service.GetSharedItem(data.ItemToken)!.Id);
        Assert.Equal(data.ItemId, Assert.Single(service.GetSharedOutfit(data.OutfitToken)!.Items).Id);
        Assert.Equal("selected-photo", service.GetSharedPhoto(data.ItemToken));
        Assert.Equal("selected-photo", service.GetSharedPhoto(data.OutfitToken, data.ItemId));
        Assert.Null(service.GetSharedPhoto(data.OutfitToken, unrelatedId));
        Assert.All(materialized.Entities.OfType<WardrobeItem>(), item => Assert.Equal(data.ItemId, item.Id));
        Assert.All(materialized.Entities.OfType<Outfit>(), outfit => Assert.Equal(data.OutfitId, outfit.Id));
        Assert.All(materialized.Entities.OfType<ShareLink>(), link =>
            Assert.Contains(link.Token, new[] { data.ItemToken, data.OutfitToken }));
        Assert.Empty(read.ChangeTracker.Entries());

        materialized.Entities.Clear();
        var missing = Guid.NewGuid();
        var engagement = new PostEngagement(new EfWardrobeRepository(read), new EfPostInteractionStore(read), TimeProvider.System);
        Assert.Null(service.GetPublicPhoto(unrelatedId));
        Assert.Null(service.GetPublicPhoto(missing));
        Assert.Null(service.GetPublicOutfitPhoto(missing, data.ItemId));
        Assert.Throws<KeyNotFoundException>(() => engagement.Read(unrelatedId, false, null, null));
        Assert.Throws<KeyNotFoundException>(() => engagement.Comments(missing, true, null, null, null, 1));
        Assert.Empty(materialized.Entities);

        Assert.Equal("selected-photo", service.GetPublicOutfitPhoto(data.OutfitId, data.ItemId));
        Assert.Equal(0, engagement.Read(data.OutfitId, true, null, null).Comments);
        Assert.All(materialized.Entities.OfType<WardrobeItem>(), item => Assert.Equal(data.ItemId, item.Id));
        Assert.All(materialized.Entities.OfType<Outfit>(), outfit => Assert.Equal(data.OutfitId, outfit.Id));
        Assert.Empty(read.ChangeTracker.Entries());
    }

    private sealed class MaterializedEntities : IMaterializationInterceptor
    {
        public List<object> Entities { get; } = [];
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            Entities.Add(entity);
            return entity;
        }
    }
}
