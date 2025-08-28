using System.Net;
using System.Net.Http.Json;
using Gateway.ControlPlane.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.IntegrationTests;

public class ControlPlaneTests
{
    [Fact]
    public async Task Routes_Etag_And_Audit_Work()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var route = new RouteConfig { Id = "r1", Path = "/r1", Destination = "http://e" };
        var create = await client.PostAsJsonAsync("/cp/routes", route);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var etag = create.Headers.ETag!.Tag;

        var proxy = await client.GetAsync("/r1");
        Assert.Equal(HttpStatusCode.BadGateway, proxy.StatusCode);

        route = route with { Destination = "http://e2" };
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/cp/routes/r1") { Content = JsonContent.Create(route) };
        putReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var update = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var newEtag = update.Headers.ETag?.Tag;

        var staleReq = new HttpRequestMessage(HttpMethod.Put, "/cp/routes/r1") { Content = JsonContent.Create(route) };
        staleReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var stale = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/cp/routes/r1");
        delReq.Headers.TryAddWithoutValidation("If-Match", newEtag);
        var del = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var missing = await client.GetAsync("/r1");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var audit = await client.GetFromJsonAsync<List<AuditEntry>>("/cp/audit");
        Assert.Contains(audit!, e => e.Resource == "route" && e.ResourceId == "r1");
    }

    [Fact]
    public async Task RateLimits_Etag_And_Audit_Work()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var plan = new RateLimitPlan { Plan = "gold", Rpm = 100 };
        var create = await client.PostAsJsonAsync("/cp/ratelimits", plan);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var etag = create.Headers.ETag!.Tag;

        plan = plan with { Rpm = 200 };
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/cp/ratelimits/gold") { Content = JsonContent.Create(plan) };
        putReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var update = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var newEtag = update.Headers.ETag?.Tag;

        var staleReq = new HttpRequestMessage(HttpMethod.Put, "/cp/ratelimits/gold") { Content = JsonContent.Create(plan) };
        staleReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var stale = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/cp/ratelimits/gold");
        delReq.Headers.TryAddWithoutValidation("If-Match", newEtag);
        var del = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var audit = await client.GetFromJsonAsync<List<AuditEntry>>("/cp/audit");
        Assert.Contains(audit!, e => e.Resource == "ratelimit" && e.ResourceId == "gold");
    }

    [Fact]
    public async Task Default_RateLimit_Can_Be_Updated_But_Not_Deleted()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var get = await client.GetAsync("/cp/ratelimits/default");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var etag = get.Headers.ETag!.Tag;
        var plan = await get.Content.ReadFromJsonAsync<RateLimitPlan>();

        var updated = plan! with { Rpm = plan.Rpm + 10 };
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/cp/ratelimits/default") { Content = JsonContent.Create(updated) };
        putReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var update = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/cp/ratelimits/default");
        delReq.Headers.TryAddWithoutValidation("If-Match", update.Headers.ETag!.Tag);
        var del = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.BadRequest, del.StatusCode);
    }

    [Fact]
    public async Task Waf_Etag_And_Audit_Work()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var toggle = new WafToggle { Rule = "xss", Enabled = true };
        var create = await client.PostAsJsonAsync("/cp/waf", toggle);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var etag = create.Headers.ETag!.Tag;

        toggle = toggle with { Enabled = false };
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/cp/waf/xss") { Content = JsonContent.Create(toggle) };
        putReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var update = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var newEtag = update.Headers.ETag?.Tag;

        var staleReq = new HttpRequestMessage(HttpMethod.Put, "/cp/waf/xss") { Content = JsonContent.Create(toggle) };
        staleReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var stale = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/cp/waf/xss");
        delReq.Headers.TryAddWithoutValidation("If-Match", newEtag);
        var del = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var audit = await client.GetFromJsonAsync<List<AuditEntry>>("/cp/audit");
        Assert.Contains(audit!, e => e.Resource == "waf" && e.ResourceId == "xss");
    }

    [Fact]
    public async Task ApiKeys_Etag_And_Audit_Work()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var key = new ApiKeyRecord { Id = "k1", Hash = "hash", Plan = "basic" };
        var create = await client.PostAsJsonAsync("/cp/apikeys", key);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var etag = create.Headers.ETag!.Tag;

        key = key with { Plan = "pro" };
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/cp/apikeys/k1") { Content = JsonContent.Create(key) };
        putReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var update = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var newEtag = update.Headers.ETag?.Tag;

        var staleReq = new HttpRequestMessage(HttpMethod.Put, "/cp/apikeys/k1") { Content = JsonContent.Create(key) };
        staleReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var stale = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/cp/apikeys/k1");
        delReq.Headers.TryAddWithoutValidation("If-Match", newEtag);
        var del = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var audit = await client.GetFromJsonAsync<List<AuditEntry>>("/cp/audit");
        Assert.Contains(audit!, e => e.Resource == "apikey" && e.ResourceId == "k1");
    }
}
