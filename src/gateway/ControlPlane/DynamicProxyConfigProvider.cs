using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;
using Yarp.ReverseProxy.Forwarder;

namespace Gateway.ControlPlane;

public sealed class DynamicProxyConfigProvider : IProxyConfigProvider
{
    private readonly IRouteStore _store;
    private InMemoryConfig _config;

    public DynamicProxyConfigProvider(IRouteStore store)
    {
        _store = store;
        _config = BuildConfig();
        _store.Changed += () =>
        {
            var old = _config;
            _config = BuildConfig();
            old.TokenSource.Cancel();
        };
    }

    public IProxyConfig GetConfig() => _config;

    private InMemoryConfig BuildConfig()
    {
        var routes = new List<Yarp.ReverseProxy.Configuration.RouteConfig>();
        var clusters = new List<ClusterConfig>();
        foreach (var r in _store.GetAll())
        {
            routes.Add(new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = r.Id,
                ClusterId = r.Id,
                Match = new RouteMatch { Path = r.Path },
                AuthorizationPolicy = r.AuthorizationPolicy,
                Transforms = string.IsNullOrEmpty(r.PathRemovePrefix)
                    ? Array.Empty<Dictionary<string, string>>()
                    : new List<Dictionary<string, string>>
                    {
                        new() { ["PathRemovePrefix"] = r.PathRemovePrefix! }
                    }
            });
            clusters.Add(new ClusterConfig
            {
                ClusterId = r.Id,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["d1"] = new DestinationConfig { Address = r.Destination }
                },
                HttpRequest = r.ActivityTimeout is null ? null : new ForwarderRequestConfig { ActivityTimeout = r.ActivityTimeout }
            });
        }
        var cts = new CancellationTokenSource();
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
