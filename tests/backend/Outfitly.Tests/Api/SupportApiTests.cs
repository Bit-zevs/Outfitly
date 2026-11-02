using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
namespace Outfitly.Tests.Api;
public sealed class SupportApiTests
{
    private static MultipartFormDataContent Form(string text = "Message", Guid? revision = null, bool create = false, bool image = false)
    {
        var form = new MultipartFormDataContent(); form.Add(new StringContent(text), "text");
        if (create) { form.Add(new StringContent("bug"), "category"); form.Add(new StringContent("A bug <test>"), "subject"); }
        if (revision.HasValue) form.Add(new StringContent(revision.ToString()!), "revision");
        if (image)
        {
            var file = new ByteArrayContent(Image());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(file, "images", "screen.png");
        }
        return form;
    }
    private static byte[] Image()
    {
        using var bitmap = new SkiaSharp.SKBitmap(2, 2);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return encoded.ToArray();
    }
    [Fact]
    public async Task Attachments_are_private_and_failed_batch_cleans_written_files()
    {
        var admin = Guid.NewGuid(); var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        using var factory = new ApiFactory(supportAdmin: admin);
        foreach (var id in new[] { admin, owner, other }) await factory.SeedOwnerAsync(id);
        using var client = await factory.OwnerClientAsync(owner); using var staff = await factory.OwnerClientAsync(admin); using var stranger = await factory.OwnerClientAsync(other);
        using var form = Form(create: true, image: true);
        var response = await client.PostAsync("/api/support/tickets", form); response.EnsureSuccessStatusCode();
        var ticket = await response.Content.ReadFromJsonAsync<JsonElement>();
        var attachment = ticket.GetProperty("messages")[0].GetProperty("attachments")[0];
        var url = attachment.GetProperty("url").GetString();
        Assert.False(ticket.ToString().Contains("storageKey", StringComparison.OrdinalIgnoreCase));
        var photo = await client.GetAsync(url); photo.EnsureSuccessStatusCode(); Assert.Equal("no-store", photo.Headers.CacheControl!.ToString());
        (await staff.GetAsync(url)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(url)).StatusCode);
        var before = Directory.GetFiles(factory.PhotoRoot).Length;
        using var invalid = Form(create: true, image: true); var bytes = new ByteArrayContent([1, 2, 3]); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); invalid.Add(bytes, "images", "bad.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/support/tickets", invalid)).StatusCode);
        Assert.Equal(before, Directory.GetFiles(factory.PhotoRoot).Length);
        using var imagesOnly = Form("", ticket.GetProperty("revision").GetGuid(), image: true);
        (await client.PostAsync($"/api/support/tickets/{ticket.GetProperty("id").GetGuid()}/messages", imagesOnly)).EnsureSuccessStatusCode();
    }    [Fact]
    public async Task Private_tickets_support_replies_statuses_and_configured_admin_access()
    {
        var admin = Guid.NewGuid(); var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        using var factory = new ApiFactory(supportAdmin: admin);
        foreach (var id in new[] { admin, owner, other }) await factory.SeedOwnerAsync(id);
        using var client = await factory.OwnerClientAsync(owner); using var staff = await factory.OwnerClientAsync(admin); using var stranger = await factory.OwnerClientAsync(other);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/support/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/support/tickets")).StatusCode);
        using var form = Form(create: true);
        var created = await client.PostAsync("/api/support/tickets", form); created.EnsureSuccessStatusCode();
        var ticket = await created.Content.ReadFromJsonAsync<JsonElement>(); var idTicket = ticket.GetProperty("id").GetGuid();
        var path = $"/api/support/tickets/{idTicket}"; var adminPath = $"/api/admin/support/tickets/{idTicket}";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        Assert.Equal(0, (await stranger.GetFromJsonAsync<JsonElement>("/api/support/tickets")).GetProperty("total").GetInt32());
        using var answer = Form("Answer", ticket.GetProperty("revision").GetGuid());
        (await staff.PostAsync(adminPath + "/messages", answer)).EnsureSuccessStatusCode();
        ticket = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("inProgress", ticket.GetProperty("status").GetString()); Assert.Equal(2, ticket.GetProperty("messages").GetArrayLength());
        var revision = ticket.GetProperty("revision").GetGuid();
        (await staff.PatchAsJsonAsync(adminPath + "/status", new { revision, status = "closed" })).EnsureSuccessStatusCode();
        using var stale = Form("Late", revision);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path + "/messages", stale)).StatusCode);
        ticket = await client.GetFromJsonAsync<JsonElement>(path);
        using var closed = Form("Closed", ticket.GetProperty("revision").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path + "/messages", closed)).StatusCode);
        (await staff.PatchAsJsonAsync(adminPath + "/status", new { revision = ticket.GetProperty("revision").GetGuid(), status = "inProgress" })).EnsureSuccessStatusCode();
        Assert.Equal(1, (await staff.GetFromJsonAsync<JsonElement>("/api/admin/support/tickets?category=bug&status=inProgress")).GetProperty("total").GetInt32());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); using var noCsrf = Form(create: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/support/tickets", noCsrf)).StatusCode);
    }
    [Fact]
    public async Task Invalid_attachment_rolls_back_entire_ticket_and_rate_limit_is_per_account()
    {
        using var factory = new ApiFactory(); var owner = Guid.NewGuid(); await factory.SeedOwnerAsync(owner); using var client = await factory.OwnerClientAsync(owner);
        using var invalid = Form(create: true); var bytes = new ByteArrayContent([1,2,3]); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); invalid.Add(bytes, "images", "bad.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/support/tickets", invalid)).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/support/tickets")).GetProperty("total").GetInt32());
        for (var i = 0; i < 4; i++) { using var form = Form(create: true); (await client.PostAsync("/api/support/tickets", form)).EnsureSuccessStatusCode(); }
        using var limited = Form(create: true); Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/support/tickets", limited)).StatusCode);
    }
}

