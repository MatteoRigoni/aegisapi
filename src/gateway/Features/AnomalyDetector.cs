using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Concurrent;

namespace Gateway.Features;

public class AnomalyDetector : BackgroundService
{
    private readonly IRequestFeatureQueue _queue;
    private readonly AnomalyDetectionSettings _settings;
    private readonly ConcurrentDictionary<(string client, string path), SlidingWindow> _windows = new();
    private DateTime _lastPrune = DateTime.UtcNow;
    private readonly MLContext? _mlContext;
    private readonly ITransformer? _mlModel;
    private readonly PredictionEngine<AnomalyVector, AnomalyPrediction>? _predictionEngine;

    public ConcurrentBag<RequestFeature> Anomalies { get; } = new();

    public AnomalyDetector(IRequestFeatureQueue queue, IOptions<AnomalyDetectionSettings> options)
    {
        _queue = queue;
        _settings = options.Value;

        if (_settings.UseMl)
        {
            _mlContext = new MLContext();
            var data = new List<AnomalyVector>
            {
                new() { Features = new float[] {0,0,0,0} }
            };
            var train = _mlContext.Data.LoadFromEnumerable(data);
            var estimator = _settings.UseIsolationForest
                ? _mlContext.AnomalyDetection.Trainers.IsolationForest(nameof(AnomalyVector.Features))
                : _mlContext.AnomalyDetection.Trainers.RandomizedPca(nameof(AnomalyVector.Features));
            _mlModel = estimator.Fit(train);
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<AnomalyVector, AnomalyPrediction>(_mlModel);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var feature in _queue.DequeueAllAsync(stoppingToken))
        {
            if (IsAnomaly(feature))
                Anomalies.Add(feature);
        }
    }

    private bool IsAnomaly(RequestFeature feature)
    {
        var now = DateTime.UtcNow;

        if (now - _lastPrune > TimeSpan.FromSeconds(_settings.PruneIntervalSeconds))
        {
            foreach (var kv in _windows.ToArray())
            {
                if (now - kv.Value.LastEvent > TimeSpan.FromSeconds(_settings.WindowSeconds * 2))
                    _windows.TryRemove(kv.Key, out _);
            }
            _lastPrune = now;
        }

        var key = (feature.ClientId ?? "unknown", feature.Path);
        var window = _windows.GetOrAdd(key, _ => new SlidingWindow(TimeSpan.FromSeconds(_settings.WindowSeconds)));
        window.Add(feature, now);

        var rps = window.Rps;
        var four = window.FourXx;
        var five = window.FiveXx;
        var waf = window.WafHits;

        if (_settings.UseZScore && window.SampleCount > 0)
        {
            var (rMean, rStd) = window.RpsStats();
            var (fMean, fStd) = window.FourStats();
            var (fiMean, fiStd) = window.FiveStats();
            var (wMean, wStd) = window.WafStats();

            if ((rStd > 0 && rps > rMean + _settings.ZScoreK * rStd) ||
                (fStd > 0 && four > fMean + _settings.ZScoreK * fStd) ||
                (fiStd > 0 && five > fiMean + _settings.ZScoreK * fiStd) ||
                (wStd > 0 && waf > wMean + _settings.ZScoreK * wStd))
                return true;
        }
        else if (rps > _settings.RpsThreshold || four > _settings.FourXxThreshold ||
                 five > _settings.FiveXxThreshold || waf > _settings.WafThreshold)
        {
            return true;
        }

        if (_settings.UseMl && _predictionEngine is not null)
        {
            var vector = new AnomalyVector { Features = new float[] { (float)rps, four, five, waf } };
            var prediction = _predictionEngine.Predict(vector);
            return prediction.Prediction[0] == 1;
        }

        return false;
    }

    private sealed class SlidingWindow
    {
        private readonly TimeSpan _window;
        private readonly Queue<(DateTime ts, int status, bool waf)> _events = new();
        private int _four;
        private int _five;
        private int _waf;

        private double _sumRps, _sumSqRps;
        private double _sumFour, _sumSqFour;
        private double _sumFive, _sumSqFive;
        private double _sumWaf, _sumSqWaf;
        private int _samples;

        public DateTime LastEvent { get; private set; } = DateTime.MinValue;

        public SlidingWindow(TimeSpan window) => _window = window;

        public void Add(RequestFeature feature, DateTime now)
        {
            _events.Enqueue((now, feature.Status, feature.WafHit));
            if (feature.Status is >=400 and <500) _four++;
            if (feature.Status >=500) _five++;
            if (feature.WafHit) _waf++;
            LastEvent = now;
            Cleanup(now);

            var rps = Rps;
            _samples++;
            _sumRps += rps;
            _sumSqRps += rps * rps;
            _sumFour += _four;
            _sumSqFour += _four * _four;
            _sumFive += _five;
            _sumSqFive += _five * _five;
            _sumWaf += _waf;
            _sumSqWaf += _waf * _waf;
        }

        private void Cleanup(DateTime now)
        {
            while (_events.Count > 0 && now - _events.Peek().ts > _window)
            {
                var old = _events.Dequeue();
                if (old.status is >=400 and <500) _four--;
                if (old.status >=500) _five--;
                if (old.waf) _waf--;
            }
        }

        public double Rps => _events.Count / _window.TotalSeconds;
        public int FourXx => _four;
        public int FiveXx => _five;
        public int WafHits => _waf;

        public int SampleCount => _samples;

        private (double mean, double std) Stats(double sum, double sumSq)
        {
            if (_samples == 0) return (0, 0);
            var mean = sum / _samples;
            var variance = sumSq / _samples - mean * mean;
            return (mean, Math.Sqrt(Math.Max(0, variance)));
        }

        public (double mean, double std) RpsStats() => Stats(_sumRps, _sumSqRps);
        public (double mean, double std) FourStats() => Stats(_sumFour, _sumSqFour);
        public (double mean, double std) FiveStats() => Stats(_sumFive, _sumSqFive);
        public (double mean, double std) WafStats() => Stats(_sumWaf, _sumSqWaf);
    }

    private class AnomalyVector
    {
        [VectorType(4)]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    private class AnomalyPrediction
    {
        [VectorType(1)]
        public float[] Prediction { get; set; } = Array.Empty<float>();
    }
}
