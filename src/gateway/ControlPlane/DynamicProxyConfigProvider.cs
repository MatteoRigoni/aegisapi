using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;

namespace Gateway.ControlPlane;

public sealed class DynamicProxyConfigProvider : IProxyConfigProvider
{
    private readonly IRouteStore _store;
    private InMemoryConfig _config;

    public DynamicProxyConfigProvider(IRouteStore store)
    {
        _store = store;
        _config = BuildConfig();
        _store.Changed += () => _config = BuildConfig(_config);
    }

    public IProxyConfig GetConfig() => _config;

    private InMemoryConfig BuildConfig(InMemoryConfig? oldConfig = null)
    {
        var routes = new List<Yarp.ReverseProxy.Configuration.RouteConfig>();
        var clusters = new List<ClusterConfig>();
        foreach (var r in _store.GetAll())
        {
            routes.Add(new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = r.Id,
                ClusterId = r.Id,
                Match = new RouteMatch { Path = r.Path }
            });
            clusters.Add(new ClusterConfig
            {
                ClusterId = r.Id,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["d1"] = new DestinationConfig { Address = r.Destination }
                }
            });
        }
        var cts = new CancellationTokenSource();
        oldConfig?.TokenSource.Cancel();
        return new InMemoryConfig(routes, clusters, new CancellationChangeToken(cts.Token), cts);
    }

    private sealed class InMemoryConfig : IProxyConfig
    {
        public InMemoryConfig(IReadOnlyList<Yarp.ReverseProxy.Configuration.RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters, IChangeToken changeToken, CancellationTokenSource tokenSource)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = changeToken;
            TokenSource = tokenSource;
        }
        public IReadOnlyList<Yarp.ReverseProxy.Configuration.RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }
        public CancellationTokenSource TokenSource { get; }
    }
}
