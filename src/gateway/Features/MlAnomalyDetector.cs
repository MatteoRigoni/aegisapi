using Microsoft.ML;
using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using System.IO;

namespace Gateway.Features;

public class MlAnomalyDetector : IAnomalyDetector, IDisposable
{
    private readonly AnomalyDetectionSettings _settings;
    private readonly MLContext _ml = new();
    private readonly DefaultObjectPoolProvider _poolProvider = new();
    private readonly ModelHolder _holder = new();
    private readonly ConcurrentQueue<(DateTime ts, float[] vec)> _buffer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _retrainLoop;
    private int _retraining;
    private bool _trained;

    public MlAnomalyDetector(IOptions<AnomalyDetectionSettings> options)
    {
        _settings = options.Value;
        TryLoadModel();
        _retrainLoop = Task.Run(() => RetrainLoopAsync(_cts.Token));
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

        var pool = _holder.Pool;
        if (pool is null)
            return false;
        var engine = pool.Get();
        AnomalyPrediction prediction;
        try
        {
            prediction = engine.Predict(new AnomalyVector { Features = vector });
        }
        finally
        {
            pool.Return(engine);
        }
        if (prediction.Score > _holder.Threshold)
        {
            reason = "ml_outlier";
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
        var pipeline = _ml.Transforms.NormalizeMeanVariance(nameof(AnomalyVector.Features))
            .Append(_settings.UseIsolationForest
                ? _ml.AnomalyDetection.Trainers.IsolationForest(nameof(AnomalyVector.Features))
                : _ml.AnomalyDetection.Trainers.RandomizedPca(nameof(AnomalyVector.Features)));
        var model = pipeline.Fit(dv);
        var pool = _poolProvider.Create(new PredictionEnginePooledObjectPolicy(_ml, model));
        var scores = data.Select(d =>
        {
            var eng = pool.Get();
            try { return eng.Predict(d).Score; }
            finally { pool.Return(eng); }
        }).OrderBy(s => s).ToArray();
        var idx = (int)Math.Floor(_settings.ScoreQuantile * (scores.Length - 1));
        var threshold = scores[idx];
        _holder.Swap(pool, threshold);
        SaveModel(model, threshold);
        _trained = true;
    }

    private async Task RetrainAsync()
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
        var pipeline = _ml.Transforms.NormalizeMeanVariance(nameof(AnomalyVector.Features))
            .Append(_settings.UseIsolationForest
                ? _ml.AnomalyDetection.Trainers.IsolationForest(nameof(AnomalyVector.Features))
                : _ml.AnomalyDetection.Trainers.RandomizedPca(nameof(AnomalyVector.Features)));
        var model = pipeline.Fit(dv);
        var pool = _poolProvider.Create(new PredictionEnginePooledObjectPolicy(_ml, model));
        var scores = data.Select(d =>
        {
            var eng = pool.Get();
            try { return eng.Predict(d).Score; }
            finally { pool.Return(eng); }
        }).OrderBy(s => s).ToArray();
        var idx = (int)Math.Floor(_settings.ScoreQuantile * (scores.Length - 1));
        var threshold = scores[idx];
        _holder.Swap(pool, threshold);
        SaveModel(model, threshold);
        await Task.CompletedTask;
    }

    private async Task RetrainLoopAsync(CancellationToken token)
    {
        var periodic = new PeriodicTimer(TimeSpan.FromMinutes(_settings.RetrainIntervalMinutes));
        while (await periodic.WaitForNextTickAsync(token))
        {
            if (Interlocked.Exchange(ref _retraining, 1) == 1)
                continue;
            try { await RetrainAsync(); }
            finally { Interlocked.Exchange(ref _retraining, 0); }
        }
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
            f.WafHit ? 1 : 0,
            (float)f.UaEntropy,
            string.Equals(f.Method, "POST", StringComparison.OrdinalIgnoreCase) ? 1f : 0f
        };

    public void Dispose()
    {
        _cts.Cancel();
        try { _retrainLoop.Wait(); } catch { }
        _cts.Dispose();
    }

    private void SaveModel(ITransformer model, double threshold)
    {
        try
        {
            using var fs = File.Create(_settings.ModelPath);
            _ml.Model.Save(model, null, fs);
            File.WriteAllText(_settings.ThresholdPath, threshold.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            // best effort
        }
    }

    private void TryLoadModel()
    {
        try
        {
            if (File.Exists(_settings.ModelPath) && File.Exists(_settings.ThresholdPath))
            {
                using var fs = File.OpenRead(_settings.ModelPath);
                var model = _ml.Model.Load(fs, out _);
                var threshold = double.Parse(File.ReadAllText(_settings.ThresholdPath), CultureInfo.InvariantCulture);
                var pool = _poolProvider.Create(new PredictionEnginePooledObjectPolicy(_ml, model));
                _holder.Swap(pool, threshold);
                _trained = true;
            }
        }
        catch
        {
            // ignore
        }
    }

    private sealed class PredictionEnginePooledObjectPolicy : IPooledObjectPolicy<PredictionEngine<AnomalyVector, AnomalyPrediction>>
    {
        private readonly MLContext _ml;
        private readonly ITransformer _model;
        public PredictionEnginePooledObjectPolicy(MLContext ml, ITransformer model)
        {
            _ml = ml;
            _model = model;
        }
        public PredictionEngine<AnomalyVector, AnomalyPrediction> Create()
            => _ml.Model.CreatePredictionEngine<AnomalyVector, AnomalyPrediction>(_model);
        public bool Return(PredictionEngine<AnomalyVector, AnomalyPrediction> obj) => true;
    }
}
