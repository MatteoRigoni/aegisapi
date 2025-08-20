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
        var key = (feature.ClientId ?? "unknown", feature.Path);
        var window = _windows.GetOrAdd(key, _ => new SlidingWindow(TimeSpan.FromSeconds(_settings.WindowSeconds)));
        window.Add(feature, now);

        var rps = window.Rps;
        var four = window.FourXx;
        var five = window.FiveXx;
        var waf = window.WafHits;

        if (rps > _settings.RpsThreshold || four > _settings.FourXxThreshold ||
            five > _settings.FiveXxThreshold || waf > _settings.WafThreshold)
            return true;

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

        public SlidingWindow(TimeSpan window) => _window = window;

        public void Add(RequestFeature feature, DateTime now)
        {
            _events.Enqueue((now, feature.Status, feature.WafHit));
            if (feature.Status is >=400 and <500) _four++;
            if (feature.Status >=500) _five++;
            if (feature.WafHit) _waf++;
            Cleanup(now);
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
