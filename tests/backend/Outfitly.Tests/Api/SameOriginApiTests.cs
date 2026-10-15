using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Outfitly.Tests.Api;

public sealed class SameOriginApiTests
{
    [Fact]
    public async Task Frontend_authentication_csrf_and_upload_work_at_the_same_https_origin()
    {
        using var factory = new ApiFactory(testAuthentication: false, frontend: true);
        await factory.InitializeDatabaseAsync();
        using var browser = factory.CreateClient();
        var home = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal("text/html", home.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Outfitly test frontend", await home.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/main.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/unknown-route")).StatusCode);
        await ApiFactory.SetCsrfAsync(browser);
        const string password = "a long password with spaces";
        (await browser.PostAsJsonAsync("/api/auth/register", new { email = "photo@example.com", password, displayName = "Owner" })).EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(browser, "photo@example.com");
        (await browser.PostAsJsonAsync("/api/auth/login", new { email = "photo@example.com", password })).EnsureSuccessStatusCode();
        await ApiFactory.SetCsrfAsync(browser);
        var id = await PhotoApiTests.CreateItem(browser);
        (await PhotoApiTests.Upload(browser, id)).EnsureSuccessStatusCode();
        var saved = await browser.GetFromJsonAsync<JsonElement>($"/api/items/{id}");
        var photoUrl = saved.GetProperty("photoUrl").GetString()!;
        Assert.StartsWith("/api/", photoUrl);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(photoUrl)).StatusCode);
        (await browser.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(photoUrl)).StatusCode);
    }
}
