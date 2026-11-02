using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace Outfitly.Tests.Api;
public sealed class CalendarApiTests
{
    [Fact]
    public async Task Calendar_is_private_idempotent_and_tracks_current_outfits()
    {
        using var factory = new ApiFactory(); var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await factory.SeedOwnerAsync(owner); await factory.SeedOwnerAsync(other);
        using var client = await factory.OwnerClientAsync(owner); using var stranger = await factory.OwnerClientAsync(other);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/calendar?month=2028-02")).StatusCode);
        var created = await client.PostAsJsonAsync("/api/outfits", new { name = "First" }); created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var second = await client.PostAsJsonAsync("/api/outfits", new { name = "Second" }); second.EnsureSuccessStatusCode();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var route = $"/api/calendar/2028-02-29/outfits/{id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PutAsync(route, null)).StatusCode);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync(route, null)).StatusCode);
        (await client.PutAsync($"/api/calendar/2028-02-29/outfits/{secondId}", null)).EnsureSuccessStatusCode();
        (await client.PutAsync($"/api/calendar/2028-03-01/outfits/{id}", null)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/outfits/{id}", new { name = "Changed" })).EnsureSuccessStatusCode();
        var month = await client.GetFromJsonAsync<JsonElement>("/api/calendar?month=2028-02");
        Assert.Equal(2, month.GetArrayLength());
        Assert.Contains(month.EnumerateArray(), x => x.GetProperty("outfit").GetProperty("name").GetString() == "Changed");
        Assert.Equal(0, (await stranger.GetFromJsonAsync<JsonElement>("/api/calendar?month=2028-02")).GetArrayLength());
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(route)).StatusCode);
        (await client.DeleteAsync($"/api/outfits/{secondId}")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/calendar?month=2028-02")).GetArrayLength());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/calendar?month=2028-03")).GetArrayLength());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync(route)).StatusCode);
    }
    [Theory]
    [InlineData("/api/calendar?month=2027-02-01")]
    [InlineData("/api/calendar?month=2027-13")]
    [InlineData("/api/calendar?month=2027-02&month=2027-03")]
    [InlineData("/api/calendar")]
    public async Task Invalid_month_is_bad_request(string path)
    {
        using var factory = new ApiFactory(); var owner = Guid.NewGuid(); await factory.SeedOwnerAsync(owner);
        using var client = await factory.OwnerClientAsync(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"/api/calendar/2027-02-29/outfits/{Guid.NewGuid()}", null)).StatusCode);
    }
}
