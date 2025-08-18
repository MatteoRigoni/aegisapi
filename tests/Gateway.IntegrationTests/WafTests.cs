using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace Gateway.IntegrationTests;

public class WafTests
{
    private static WebApplicationFactory<Program> CreateFactory(IDictionary<string, string?>? extra = null)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                if (extra is not null)
                {
                    builder.ConfigureAppConfiguration((_, cfg) =>
                    {
                        cfg.AddInMemoryCollection(extra);
                    });
                }
            });

    [Fact]
    public async Task Benign_Request_Passes()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz?input=test");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task SqlInjection_Is_Blocked()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz?input=' OR 1=1 --");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Xss_Is_Blocked()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz?input=<script>alert(1)</script>");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PathTraversal_Is_Blocked()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz?file=../../etc/passwd");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Ssrf_Is_Blocked()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz?url=http://169.254.169.254/latest/meta-data");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Disabled_Rule_Allows_Request()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Waf:SqlInjection"] = "false"
        });
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz?input=' OR 1=1 --");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
