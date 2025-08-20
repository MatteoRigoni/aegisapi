using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ObjectPool;

namespace Gateway.Features;

/// <summary>
/// ML-based anomaly detector with:
/// - Warm-up baseline collection
/// - PCA training (rank clamped to feature dimension)
/// - Zero-variance fallback (no ML pipeline; Score=RPS)
/// - Quantile-based threshold calibration
/// - Periodic re-calibration/re-train
/// - Model/threshold persistence and immediate reload
/// - Thread-safe prediction via ObjectPool
/// </summary>
public class MlAnomalyDetector : IAnomalyDetector, IDisposable
{
    private readonly AnomalyDetectionSettings _settings;
    private readonly MLContext _ml = new();
    private readonly DefaultObjectPoolProvider _poolProvider = new();
    private readonly ModelHolder _holder = new();
    private readonly ConcurrentQueue<(DateTime ts, float[] vec)> _buffer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _retrainLoop;

    private volatile bool _trained;
    private volatile bool _fallbackActive; // true => Score computed in code (no model)
    private int _retraining;

    public MlAnomalyDetector(IOptions<AnomalyDetectionSettings> options)
    {
        _settings = options.Value ?? throw new ArgumentNullException(nameof(options));

        // IsolationForest is not supported natively here; keep fail-fast to avoid silent misconfig.
        if (_settings.UseIsolationForest)
            throw new NotImplementedException("IsolationForest is not supported in ML.NET. Use ONNX scoring if needed.");

        TryLoadPersistedState();
        _retrainLoop = Task.Run(() => RetrainLoopAsync(_cts.Token));
    }

    public bool Observe(RequestFeature feature, out string reason)
    {
        reason = string.Empty;

        // Collect baseline until trained. Exclude dirty samples from baseline.
        var vector = ToVector(feature);
        if (!_trained)
        {
            if (!feature.WafHit && !feature.SchemaError)
                EnqueueBounded(vector);

            if (_buffer.Count >= _settings.BaselineSampleSize)
                TrainBaseline();

            return false;
        }

        // Scoring: fallback (Score = RPS) or PCA model
        float score;
        if (_fallbackActive)
        {
            score = (vector.Length > 0) ? vector[0] : 0f;
        }
        else
        {
            var pool = _holder.Pool;
            if (pool is null) return false;

            var engine = pool.Get();
            try { score = engine.Predict(new AnomalyVector { Features = vector }).Score; }
            finally { pool.Return(engine); }
        }

        // Decision
        if (score > _holder.Threshold)
        {
            reason = "ml_outlier";
            return true;
        }

        // Keep collecting clean samples for future re-train
        if (!feature.WafHit && !feature.SchemaError)
            EnqueueBounded(vector);

        return false;
    }

    // ---- Training / Retraining ------------------------------------------------

    private void TrainBaseline()
    {
        var data = _buffer.Select(b => new AnomalyVector { Features = b.vec }).ToList();
        if (data.Count == 0) return;

        var dv = _ml.Data.LoadFromEnumerable(data);

        // If variance is too low (e.g., identical samples), use fallback (no ML pipeline).
        if (Variance(data) < _settings.MinVarianceGuard)
        {
            CalibrateFallback(data);
            _trained = true;
            return;
        }

        // PCA pipeline with safe rank based on schema dimension
        var pipeline = BuildPcaPipeline(dv);
        var model = pipeline.Fit(dv);

        var pool = _poolProvider.Create(new PredictionEnginePooledObjectPolicy(_ml, model));
        var scores = data.Select(d =>
        {
            var eng = pool.Get();
            try { return eng.Predict(d).Score; }
            finally { pool.Return(eng); }
        }).OrderBy(s => s).ToArray();

        var threshold = Quantile(scores, ClampQuantile(_settings.ScoreQuantile));
        _holder.Swap(pool, threshold);
        SaveModelAndThreshold(model, dv.Schema, threshold);

        _fallbackActive = false;
        _trained = true;
    }

    private async Task RetrainAsync()
    {
        if (!_trained) return;

        // Sliding window and guards
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(_settings.TrainingWindowMinutes);
        while (_buffer.TryPeek(out var item) && item.ts < cutoff)
            _buffer.TryDequeue(out _);

        if (_buffer.Count < _settings.MinSamplesGuard)
            return;

        var data = _buffer.ToArray().Select(b => new AnomalyVector { Features = b.vec }).ToList();
        if (data.Count == 0) return;

        var dv = _ml.Data.LoadFromEnumerable(data);
        var varOk = Variance(data) >= _settings.MinVarianceGuard;

        // If we are in fallback and data still has low variance, just recalibrate threshold on RPS.
        if (_fallbackActive && !varOk)
        {
            CalibrateFallback(data);
            return;
        }

        // If we are in fallback and now variance is OK -> switch to PCA.
        if (_fallbackActive && varOk)
        {
            var pipeline = BuildPcaPipeline(dv);
            var model = pipeline.Fit(dv);

            var pool = _poolProvider.Create(new PredictionEnginePooledObjectPolicy(_ml, model));
            var scores = data.Select(d =>
            {
                var eng = pool.Get();
                try { return eng.Predict(d).Score; }
                finally { pool.Return(eng); }
            }).OrderBy(s => s).ToArray();

            var threshold = Quantile(scores, ClampQuantile(_settings.ScoreQuantile));
            _holder.Swap(pool, threshold);
            SaveModelAndThreshold(model, dv.Schema, threshold);

            _fallbackActive = false;
            return;
        }

        // If currently PCA and variance is too low, skip retrain to keep the last good model.
        if (!_fallbackActive && !varOk)
            return;

        // PCA retrain
        var pcaPipeline = BuildPcaPipeline(dv);
        var pcaModel = pcaPipeline.Fit(dv);

        var pcaPool = _poolProvider.Create(new PredictionEnginePooledObjectPolicy(_ml, pcaModel));
        var pcaScores = data.Select(d =>
        {
            var eng = pcaPool.Get();
            try { return eng.Predict(d).Score; }
            finally { pcaPool.Return(eng); }
        }).OrderBy(s => s).ToArray();

        var pcaThreshold = Quantile(pcaScores, ClampQuantile(_settings.ScoreQuantile));
        _holder.Swap(pcaPool, pcaThreshold);
        SaveModelAndThreshold(pcaModel, dv.Schema, pcaThreshold);

        await Task.CompletedTask;
    }

    private async Task RetrainLoopAsync(CancellationToken token)
    {
        var periodic = new PeriodicTimer(TimeSpan.FromMinutes(_settings.RetrainIntervalMinutes));
        while (await periodic.WaitForNextTickAsync(token))
        {
            if (Interlocked.Exchange(ref _retraining, 1) == 1) continue;
            try { await RetrainAsync(); }
            finally { Interlocked.Exchange(ref _retraining, 0); }
        }
    }

    // ---- Helpers --------------------------------------------------------------

    private IEstimator<ITransformer> BuildPcaPipeline(IDataView dv)
    {
        var featureCol = nameof(AnomalyVector.Features);

        // Infer vector dimension from schema and clamp PCA rank accordingly.
        var vecType = dv.Schema[featureCol].Type as VectorDataViewType;
        var dim = vecType?.Size ?? 0;
        var wanted = 5;
        var rank = Math.Max(2, dim > 0 ? Math.Min(wanted, dim) : wanted);

        var pca = new RandomizedPcaTrainer.Options
        {
            FeatureColumnName = featureCol,
            Rank = rank,
            EnsureZeroMean = true,
            // Conservative oversampling; reduces chance of numerical issues on small dims.
            Oversampling = Math.Min(3, rank)
        };

        return _ml.Transforms
                  .NormalizeMeanVariance(featureCol)
                  .Append(_ml.AnomalyDetection.Trainers.RandomizedPca(pca));
    }

    private void CalibrateFallback(System.Collections.Generic.IEnumerable<AnomalyVector> data)
    {
        // Score = RPS (feature[0]); persist only threshold + a fallback marker.
        var scores = data.Select(d => (d.Features != null && d.Features.Length > 0) ? d.Features[0] : 0f)
                         .OrderBy(s => s)
                         .ToArray();

        var threshold = Quantile(scores, ClampQuantile(_settings.ScoreQuantile));
        _holder.Swap(pool: null, threshold);
        SaveFallbackThreshold(threshold);

        _fallbackActive = true;
    }

    private static double Variance(System.Collections.Generic.IEnumerable<AnomalyVector> data)
    {
        var arr = data.Select(d => d.Features ?? Array.Empty<float>()).ToArray();
        if (arr.Length == 0) return 0;

        var dim = arr[0].Length;
        if (dim == 0) return 0;

        var means = new double[dim];
        foreach (var vec in arr)
            for (int i = 0; i < dim; i++) means[i] += vec[i];
        for (int i = 0; i < dim; i++) means[i] /= arr.Length;

        double sum = 0;
        foreach (var vec in arr)
            for (int i = 0; i < dim; i++)
            {
                var diff = vec[i] - means[i];
                sum += diff * diff;
            }
        return sum / arr.Length;
    }

    private static double Quantile(float[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        var idx = (int)Math.Floor(q * (sorted.Length - 1));
        idx = Math.Clamp(idx, 0, sorted.Length - 1);
        return sorted[idx];
    }

    private static double ClampQuantile(double q)
    {
        // Avoid exactly 1.0; keep within [0.0, 0.999999] for safe indexing.
        if (double.IsNaN(q) || double.IsInfinity(q)) return 0.995;
        return Math.Max(0.0, Math.Min(0.999999, q));
    }

    private static float[] ToVector(RequestFeature f) => new float[]
    {
        (float)f.RpsWindow,
        f.Status is >= 400 and < 500 ? 1 : 0,
        f.Status >= 500 ? 1 : 0,
        f.WafHit ? 1 : 0,
        (float)f.UaEntropy,
        string.Equals(f.Method, "POST", StringComparison.OrdinalIgnoreCase) ? 1f : 0f
    };

    private void EnqueueBounded(float[] vec)
    {
        _buffer.Enqueue((DateTime.UtcNow, vec));
        // Simple hard cap to prevent unbounded growth under pressure.
        var max = Math.Max(_settings.MinSamplesGuard * 100, 50_000);
        while (_buffer.Count > max) _buffer.TryDequeue(out _);
    }

    // ---- Persistence ----------------------------------------------------------

    private void SaveModelAndThreshold(ITransformer model, DataViewSchema schema, double threshold)
    {
        try
        {
            using var fs = File.Create(_settings.ModelPath);
            _ml.Model.Save(model, schema, fs);
            File.WriteAllText(_settings.ThresholdPath, threshold.ToString(CultureInfo.InvariantCulture));
            // Remove fallback marker if switching from fallback to PCA.
            var marker = _settings.ModelPath + ".fallback";
            if (File.Exists(marker)) File.Delete(marker);
        }
        catch
        {
            // best effort persistence; detector keeps working in-memory
        }
    }

    private void SaveFallbackThreshold(double threshold)
    {
        try
        {
            File.WriteAllText(_settings.ThresholdPath, threshold.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(_settings.ModelPath + ".fallback", "FALLBACK");
        }
        catch
        {
            // best effort persistence; detector keeps working in-memory
        }
    }

    private void TryLoadPersistedState()
    {
        try
        {
            var marker = _settings.ModelPath + ".fallback";

            // Fallback persisted state
            if (File.Exists(marker) && File.Exists(_settings.ThresholdPath))
            {
                var thr = double.Parse(File.ReadAllText(_settings.ThresholdPath), CultureInfo.InvariantCulture);
                _holder.Swap(pool: null, thr);
                _fallbackActive = true;
                _trained = true;
                return;
            }

            // PCA persisted state
            if (File.Exists(_settings.ModelPath) && File.Exists(_settings.ThresholdPath))
            {
                using var fs = File.OpenRead(_settings.ModelPath);
                var model = _ml.Model.Load(fs, out _);
                var thr = double.Parse(File.ReadAllText(_settings.ThresholdPath), CultureInfo.InvariantCulture);
                var pool = _poolProvider.Create(new PredictionEnginePooledObjectPolicy(_ml, model));
                _holder.Swap(pool, thr);
                _fallbackActive = false;
                _trained = true;
            }
        }
        catch
        {
            // If anything goes wrong, we remain in warm-up and will rebuild
            _fallbackActive = false;
            _trained = false;
        }
    }

    // ---- Lifecycle ------------------------------------------------------------

    public void Dispose()
    {
        _cts.Cancel();
        try { _retrainLoop.Wait(); } catch { /* swallow on dispose */ }
        _cts.Dispose();
    }

    // Pool policy for PredictionEngine (not thread-safe → pooled).
    private sealed class PredictionEnginePooledObjectPolicy
        : IPooledObjectPolicy<PredictionEngine<AnomalyVector, AnomalyPrediction>>
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
