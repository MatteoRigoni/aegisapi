using Gateway.AI;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Gateway.IntegrationTests;

public class SummarizerClientTests
{
    [Fact]
    public async Task SummarizeAsync_PostsAndParses()
    {
        var handler = new FakeHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var sut = new SummarizerHttpClient(client);
        var bundle = new IncidentBundle("dev","Rules","reason", new List<FeatureEventLite>(), new Dictionary<string,double>(), new Dictionary<string,int>(), null);
        var res = await sut.SummarizeAsync(bundle);
        Assert.NotNull(handler.Request);
        Assert.Equal("/ai/summarize", handler.Request!.RequestUri!.AbsolutePath);
        Assert.NotNull(res);
        Assert.Equal("ok", res!.Summary);
    }

    private class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var json = JsonSerializer.Serialize(new SummaryResponse("ok","cause",null,0.1,Array.Empty<string>()));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
