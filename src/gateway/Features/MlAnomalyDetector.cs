using Microsoft.ML;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Gateway.Features;

public class MlAnomalyDetector : IAnomalyDetector, IDisposable
{
    private readonly AnomalyDetectionSettings _settings;
    private readonly MLContext _ml = new();
    private readonly ModelHolder _holder = new();
    private readonly ConcurrentQueue<(DateTime ts, float[] vec)> _buffer = new();
    private readonly Timer _timer;
    private bool _trained;

    public MlAnomalyDetector(IOptions<AnomalyDetectionSettings> options)
    {
        _settings = options.Value;
        _timer = new Timer(_ => Retrain(), null,
            TimeSpan.FromMinutes(_settings.RetrainIntervalMinutes),
            TimeSpan.FromMinutes(_settings.RetrainIntervalMinutes));
    }

    public bool Observe(RequestFeature feature, out string reason)
    {
        reason = string.Empty;
        var vector = ToVector(feature);

        if (!_trained)
        {
            if (!feature.WafHit && !feature.SchemaError)
            {
                _buffer.Enqueue((DateTime.UtcNow, vector));
                if (_buffer.Count >= _settings.BaselineSampleSize)
                    TrainBaseline();
            }
            return false;
        }

        var engine = _holder.Engine;
        if (engine is null)
            return false;

        var prediction = engine.Predict(new AnomalyVector { Features = vector });
        if (prediction.Score > _holder.Threshold)
        {
            reason = "ml anomaly";
            return true;
        }

        if (!feature.WafHit && !feature.SchemaError)
            _buffer.Enqueue((DateTime.UtcNow, vector));

        return false;
    }

    private void TrainBaseline()
    {
        var data = _buffer.Select(b => new AnomalyVector { Features = b.vec }).ToList();
        var dv = _ml.Data.LoadFromEnumerable(data);
        var model = _ml.AnomalyDetection.Trainers.RandomizedPca(nameof(AnomalyVector.Features)).Fit(dv);
        var engine = _ml.Model.CreatePredictionEngine<AnomalyVector, AnomalyPrediction>(model);
        var scores = data.Select(d => engine.Predict(d).Score).OrderBy(s => s).ToArray();
        var idx = (int)Math.Floor(_settings.ScoreQuantile * (scores.Length - 1));
        var threshold = scores[idx];
        _holder.Swap(engine, threshold);
        _trained = true;
    }

    private void Retrain()
    {
        if (!_trained) return;
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(_settings.TrainingWindowMinutes);
        while (_buffer.TryPeek(out var item) && item.ts < cutoff)
            _buffer.TryDequeue(out _);
        if (_buffer.Count < _settings.MinSamplesGuard)
            return;
        var data = _buffer.ToArray().Select(b => new AnomalyVector { Features = b.vec }).ToList();
        if (Variance(data) < _settings.MinVarianceGuard)
            return;
        var dv = _ml.Data.LoadFromEnumerable(data);
        var model = _ml.AnomalyDetection.Trainers.RandomizedPca(nameof(AnomalyVector.Features)).Fit(dv);
        var engine = _ml.Model.CreatePredictionEngine<AnomalyVector, AnomalyPrediction>(model);
        var scores = data.Select(d => engine.Predict(d).Score).OrderBy(s => s).ToArray();
        var idx = (int)Math.Floor(_settings.ScoreQuantile * (scores.Length - 1));
        var threshold = scores[idx];
        _holder.Swap(engine, threshold);
    }

    private static double Variance(IEnumerable<AnomalyVector> data)
    {
        var arr = data.Select(d => d.Features).ToArray();
        if (arr.Length == 0) return 0;
        var means = new double[arr[0].Length];
        foreach (var vec in arr)
            for (int i = 0; i < vec.Length; i++) means[i] += vec[i];
        for (int i = 0; i < means.Length; i++) means[i] /= arr.Length;
        double sum = 0;
        foreach (var vec in arr)
            for (int i = 0; i < vec.Length; i++)
            {
                var diff = vec[i] - means[i];
                sum += diff * diff;
            }
        return sum / arr.Length;
    }

    private static float[] ToVector(RequestFeature f)
        => new float[]
        {
            (float)f.RpsWindow,
            f.Status is >=400 and <500 ? 1 : 0,
            f.Status >=500 ? 1 : 0,
            f.WafHit ? 1 : 0
        };

    public void Dispose()
    {
        _timer.Dispose();
    }
}
