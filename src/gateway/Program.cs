using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);

// YARP reverse proxy: initial /api/* route to local backend
builder.Services.AddReverseProxy()
    .LoadFromMemory(
        new[]
        {
            new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = "api",
                ClusterId = "backend",
                Match = new() { Path = "/api/{**catch-all}" }
            }
        },
        new[]
        {
            new Yarp.ReverseProxy.Configuration.ClusterConfig
            {
                ClusterId = "backend",
                Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
                {
                    ["d1"] = new() { Address = "http://localhost:5005/" }
                }
            }
        });

var app = builder.Build();
app.MapGet("/", () => "AegisAPI Gateway up");
app.MapReverseProxy();
app.Run();
