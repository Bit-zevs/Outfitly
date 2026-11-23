using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Outfitly.Tests.Api;

public sealed class ReverseProxyApiTests
{
    [Theory]
    [InlineData("172.17.0.1", "172.17.0.1", true)]
    [InlineData("172.17.0.1", "::ffff:172.17.0.1", true)]
    [InlineData("::ffff:172.17.0.1", "172.17.0.1", true)]
    [InlineData("172.17.0.1", "172.17.0.2", false)]
    [InlineData("172.17.0.1", "127.0.0.1", false)]
    [InlineData("172.17.0.1", "::1", false)]
    [InlineData("", "127.0.0.1", false)]
    public async Task ForwardedHeadersApplyOnlyToTheConfiguredProxy(string knownProxy, string peer, bool trusted)
    {
        using var factory = new ApiFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ReverseProxy:KnownProxy", knownProxy);
            builder.UseSetting("HTTPS_PORT", "443");
        });

        var context = await configured.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Host = new("localhost");
            context.Request.Path = "/health";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.1, 198.51.100.42";
            context.Request.Headers["X-Forwarded-Proto"] = "http, https";
        });

        Assert.Equal(trusted ? 200 : 307, context.Response.StatusCode);
        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse(trusted ? "198.51.100.42" : peer), context.Connection.RemoteIpAddress);
    }
}
