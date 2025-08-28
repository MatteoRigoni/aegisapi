using Gateway.AI;
using System.Collections.Concurrent;

namespace Gateway.AI
{
    public sealed class IncidentBundleFactory
    {
        private readonly int _maxEvents;
        private readonly ConcurrentQueue<FeatureEventLite> _events = new();

        public IncidentBundleFactory(int maxEvents = 50)
        {
            _maxEvents = maxEvents;
        }

        public void Add(FeatureEventLite ev)
        {
            _events.Enqueue(ev);
            while (_events.Count > _maxEvents && _events.TryDequeue(out _)) { }
        }

        public IncidentBundle Create(string environment, string reason)
        {
            var list = _events.ToArray();
            var counters = new Dictionary<string, double>
            {
                ["rps"] = list.Length,
                ["errRate"] = list.Length == 0 ? 0 : list.Count(e => e.StatusCode >= 400) / (double)list.Length
            };
            var topPaths = list
                .GroupBy(e => e.RouteKey)
                .ToDictionary(g => g.Key, g => g.Count());
            return new IncidentBundle(environment, "Rules", reason, list, counters, topPaths, null);
        }
    }
}
