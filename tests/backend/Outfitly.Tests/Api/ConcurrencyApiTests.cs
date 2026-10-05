using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
using System.Text.Json;

namespace Outfitly.Tests.Api;

public sealed class ConcurrencyApiTests
{
    [Fact]
    public async Task A_stale_command_returns_409_and_rolls_back_instead_of_claiming_success()
    {
        var beforeCommit = new BeforeCommit();
        using var factory = new ApiFactory(configureDatabase: options => options.AddInterceptors(beforeCommit));
        var owner = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner);
        using var client = factory.OwnerClient(owner);
        var itemResponse = await client.PostAsJsonAsync("/api/items", new { name = "Shirt", category = "Top" });
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var outfitResponse = await client.PostAsJsonAsync("/api/outfits", new { name = "Daily" });
        var outfit = (await outfitResponse.Content.ReadFromJsonAsync<OutfitView>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/outfits/{outfit.Id}/items/{itemId}", null)).StatusCode);
        // Let an independent scope empty the outfit after HTTP validation but before SaveChanges.
        beforeCommit.Action = async () =>
        {
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<WardrobeService>().RemoveItemFromOutfit(owner, outfit.Id, itemId);
            await scope.ServiceProvider.GetRequiredService<OutfitlyDbContext>().SaveChangesAsync();
        };

        var response = await client.PatchAsJsonAsync($"/api/outfits/{outfit.Id}/publication", new { isPublic = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var saved = (await client.GetFromJsonAsync<OutfitView>($"/api/outfits/{outfit.Id}"))!;
        Assert.Empty(saved.Items);
        Assert.False(saved.IsPublic);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync($"/api/outfits/{outfit.Id}/publication", new { isPublic = true })).StatusCode);
    }

    private sealed class BeforeCommit : SaveChangesInterceptor
    {
        internal Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var action = Action;
            Action = null;
            if (action is not null) await action();
            return result;
        }
    }
}
