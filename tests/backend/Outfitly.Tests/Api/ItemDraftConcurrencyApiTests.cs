using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Outfitly.Application;
using Outfitly.Application.Authentication;
namespace Outfitly.Tests.Api;
public sealed class ItemDraftConcurrencyApiTests
{
    [Fact]
    public async Task Metadata_saved_after_draft_validation_returns_409_and_keeps_the_draft_active()
    {
        var before = new BeforeCommit();
        using var factory = new ApiFactory(testAuthentication: false, configureDatabase: options => options.AddInterceptors(before));
        await factory.InitializeDatabaseAsync(); using var client = await SocialProfileApiTests.Register(factory, "owner");
        var item = await WorkspaceApiTests.CreateItem(client, "Original");
        var original = await client.GetFromJsonAsync<JsonElement>($"/api/items/{item}");
        var draftId = Guid.NewGuid();
        var response = await client.PutAsJsonAsync($"/api/drafts/{draftId}", new {
            kind = "items", targetId = item, baseVersion = original.GetProperty("editVersion").GetString(),
            content = new { name = "Draft name", category = "Top", photoMode = "file" }
        });
        response.EnsureSuccessStatusCode(); var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
        before.Action = async () => Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/items/{item}", new { name = "Other device", category = "Top" })).StatusCode);
        var result = await client.PostAsJsonAsync($"/api/drafts/{draftId}/save", new { version = draft.GetProperty("version").GetGuid() });
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("application/problem+json", result.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Other device", (await client.GetFromJsonAsync<JsonElement>($"/api/items/{item}")).GetProperty("name").GetString());
        var retained = await client.GetFromJsonAsync<JsonElement>($"/api/drafts/{draftId}");
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("completedId").ValueKind);
        Assert.Equal(draft.GetProperty("version").GetGuid(), retained.GetProperty("version").GetGuid());
        Assert.Equal("Draft name", retained.GetProperty("content").GetProperty("name").GetString());
    }
    private sealed class BeforeCommit : SaveChangesInterceptor
    {
        internal Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var action = Action; Action = null;
            if (action is not null) await action(); return result;
        }
    }
}
