using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Gateway.Features;

public class RollingThresholdDetector : IAnomalyDetector
{
    private readonly AnomalyDetectionSettings _settings;
    private readonly IMemoryCache _cache;

    public RollingThresholdDetector(IOptions<AnomalyDetectionSettings> options, IMemoryCache cache)
    {
        _settings = options.Value;
        _cache = cache;
    }

    public bool Observe(RequestFeature feature, out string reason)
    {
        var key = (feature.ClientId ?? "unknown", feature.RouteKey);
        var now = DateTime.UtcNow;
        var window = _cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(_settings.WindowTtlMinutes);
            return new Window(_settings.RpsWindowSeconds, _settings.ErrorWindowSeconds);
        });
        window.Add(feature, now);

        if (window.Rps > _settings.RpsThreshold)
        {
            reason = "rps_spike";
            return true;
        }
        if (window.FourXx > _settings.FourXxThreshold)
        {
            reason = "4xx_spike";
            return true;
        }
        if (window.FiveXx > _settings.FiveXxThreshold)
        {
            reason = "5xx_spike";
            return true;
        }
        if (window.WafHits > _settings.WafThreshold)
        {
            reason = "waf_spike";
            return true;
        }
        if (feature.UaEntropy < _settings.UaEntropyThreshold)
        {
            reason = "ua_low_entropy";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private sealed class Window
    {
        private readonly TimeSpan _rpsWindow;
        private readonly TimeSpan _errWindow;
        private readonly Queue<DateTime> _rpsEvents = new();
        private readonly Queue<DateTime> _fourEvents = new();
        private readonly Queue<DateTime> _fiveEvents = new();
        private readonly Queue<DateTime> _wafEvents = new();

        public Window(int rpsSeconds, int errSeconds)
        {
            _rpsWindow = TimeSpan.FromSeconds(rpsSeconds);
            _errWindow = TimeSpan.FromSeconds(errSeconds);
        }

        public void Add(RequestFeature feature, DateTime now)
        {
            _rpsEvents.Enqueue(now);
            if (feature.Status is >= 400 and < 500) _fourEvents.Enqueue(now);
            if (feature.Status >= 500) _fiveEvents.Enqueue(now);
            if (feature.WafHit) _wafEvents.Enqueue(now);
            Cleanup(now);
        }

        private void Cleanup(DateTime now)
        {
            while (_rpsEvents.Count > 0 && now - _rpsEvents.Peek() > _rpsWindow)
                _rpsEvents.Dequeue();
            while (_fourEvents.Count > 0 && now - _fourEvents.Peek() > _errWindow)
                _fourEvents.Dequeue();
            while (_fiveEvents.Count > 0 && now - _fiveEvents.Peek() > _errWindow)
                _fiveEvents.Dequeue();
            while (_wafEvents.Count > 0 && now - _wafEvents.Peek() > _errWindow)
                _wafEvents.Dequeue();
        }

        public double Rps => _rpsEvents.Count / _rpsWindow.TotalSeconds;
        public int FourXx => _fourEvents.Count;
        public int FiveXx => _fiveEvents.Count;
        public int WafHits => _wafEvents.Count;
    }
}
