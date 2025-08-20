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

        var window = _cache.GetOrCreate<Window>(key, entry =>
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
        private DateTime _lastNow; // track most recent "now" for RPS calc

        public Window(int rpsSeconds, int errSeconds)
        {
            _rpsWindow = TimeSpan.FromSeconds(Math.Max(1, rpsSeconds)); // guard
            _errWindow = TimeSpan.FromSeconds(Math.Max(1, errSeconds)); // guard
            _lastNow = DateTime.UtcNow;
        }

        public void Add(RequestFeature feature, DateTime now)
        {
            _lastNow = now;

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

        // Compute RPS over the *actual elapsed seconds* since the oldest event in window,
        // clamped to [1 second, configured rpsWindow].
        public double Rps
        {
            get
            {
                if (_rpsEvents.Count == 0) return 0d;
                var oldest = _rpsEvents.Peek();
                var elapsed = (_lastNow - oldest).TotalSeconds;

                // Clamp denominator to avoid division by near-zero and to respect configured window.
                var denom = Math.Max(1.0, Math.Min(_rpsWindow.TotalSeconds, elapsed));
                return _rpsEvents.Count / denom;
            }
        }

        public int FourXx => _fourEvents.Count;
        public int FiveXx => _fiveEvents.Count;
        public int WafHits => _wafEvents.Count;
    }
}
