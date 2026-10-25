using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Api;

public sealed class EngagementConcurrencyApiTests
{
    [Theory]
    [InlineData("view")]
    [InlineData("like")]
    [InlineData("comment")]
    public async Task Item_deleted_after_access_check_returns_conflict_without_persisting_interactions(string kind)
    {
        var beforeCommit = new BeforeCommit();
        using var factory = new ApiFactory(configureDatabase: options => options.AddInterceptors(beforeCommit));
        var ownerId = Guid.NewGuid(); var visitorId = Guid.NewGuid();
        await factory.SeedOwnerAsync(ownerId); await factory.SeedOwnerAsync(visitorId);
        using var owner = await factory.OwnerClientAsync(ownerId);
        using var visitor = await factory.OwnerClientAsync(visitorId);
        var created = await owner.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await owner.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true })).EnsureSuccessStatusCode();
        beforeCommit.Action = async () => Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/items/{id}")).StatusCode);

        var response = kind switch
        {
            "view" => await visitor.PostAsync($"/api/items/{id}/views", null),
            "like" => await visitor.PutAsync($"/api/items/{id}/like", null),
            _ => await visitor.PostAsJsonAsync($"/api/items/{id}/comments", new { text = "Comment on a disappearing item" })
        };

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/items/{id}/engagement")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>();
        Assert.False(await db.WardrobeItems.AnyAsync(x => x.Id == id));
        Assert.Empty(await db.Set<PostInteraction>().ToArrayAsync());
    }

    private sealed class BeforeCommit : SaveChangesInterceptor
    {
        internal Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var action = Action; Action = null;
            if (action is not null) await action();
            return result;
        }
    }
}
