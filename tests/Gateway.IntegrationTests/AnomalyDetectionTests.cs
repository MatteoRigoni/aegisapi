using Gateway.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Gateway.IntegrationTests;

public class AnomalyDetectionTests
{
    /// <summary>
    /// Sets up a RollingThresholdDetector with low thresholds and verifies
    /// it flags anomalies for RPS, HTTP errors, WAF hits, and low user-agent entropy.
    /// Each anomaly uses a distinct client/route to isolate detection windows.
    /// </summary>
    [Fact]
    public void RulesDetectorDetectsSpikes()
    {
        var settings = new AnomalyDetectionSettings
        {
            RpsThreshold = 5,
            FourXxThreshold = 1,
            FiveXxThreshold = 0,
            WafThreshold = 0,
            UaEntropyThreshold = 1
        };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var detector = new RollingThresholdDetector(Options.Create(settings), cache);

        // produce 6 requests quickly to exceed RPS threshold (5 over 1s)
        for (int i = 0; i < 6; i++)
            detector.Observe(new RequestFeature("c1", 0, 5, "/r1", 200, false), out _);
        Assert.True(detector.Observe(new RequestFeature("c1", 0, 5, "/r1", 200, false), out var reason) && reason == "rps_spike");

        // exceed 4xx threshold for a different client/route pair
        detector.Observe(new RequestFeature("c2", 0, 5, "/r2", 404, false), out _);
        Assert.True(detector.Observe(new RequestFeature("c2", 0, 5, "/r2", 404, false), out reason) && reason == "4xx_spike");

        // single 5xx triggers immediately when threshold is zero
        Assert.True(detector.Observe(new RequestFeature("c3", 0, 5, "/r3", 500, false), out reason) && reason == "5xx_spike");

        // WAF hit flagged instantly
        Assert.True(detector.Observe(new RequestFeature("c4", 0, 5, "/r4", 200, false, true), out reason) && reason == "waf_spike");

        // low user-agent entropy
        Assert.True(detector.Observe(new RequestFeature("c5", 0, 0, "/r5", 200, false), out reason) && reason == "ua_low_entropy");
    }

    /// <summary>
    /// Ensures the rolling detector isolates windows per client/route pair so
    /// traffic on one route does not influence another route for the same client.
    /// </summary>
    [Fact]
    public void RulesDetectorIsolatesRoutesPerClient()
    {
        var settings = new AnomalyDetectionSettings
        {
            RpsThreshold = 1
        };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var detector = new RollingThresholdDetector(Options.Create(settings), cache);

        // Trigger spike on /r1 for client "c"
        detector.Observe(new RequestFeature("c", 0, 5, "/r1", 200, false, false, "GET", "/r1"), out _);
        Assert.True(detector.Observe(new RequestFeature("c", 0, 5, "/r1", 200, false, false, "GET", "/r1"), out var reason) && reason == "rps_spike");

        // Same client but different route should not be flagged
        Assert.False(detector.Observe(new RequestFeature("c", 0, 5, "/r2", 200, false, false, "GET", "/r2"), out _));
    }

    /// <summary>
    /// Uses MlAnomalyDetector in ML mode to verify that it buffers a
    /// small baseline of nominal traffic before reporting an outlier once trained.
    /// </summary>
    [Fact]
    public void MlDetectorWarmsUpAndScores()
    {
        var settings = new AnomalyDetectionSettings
        {
            Mode = DetectionMode.Ml,
            BaselineSampleSize = 3,
            TrainingWindowMinutes = 60,
            RetrainIntervalMinutes = 60,
            UseIsolationForest = false
        };
        var ml = new MlAnomalyDetector(Options.Create(settings));

        // Warm-up with legitimate events
        for (int i = 0; i < 3; i++)
            Assert.False(ml.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _));

        // After training, anomaly with high RPS
        Assert.True(ml.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out var reason) && reason == "ml_outlier");
    }

    /// <summary>
    /// Configures a HybridDetector composed of rule and ML detectors and
    /// verifies that rule-based anomalies take precedence over ML scoring.
    /// </summary>
    [Fact]
    public void HybridAppliesRulesFirst()
    {
        var settings = new AnomalyDetectionSettings
        {
            Mode = DetectionMode.Hybrid,
            BaselineSampleSize = 3,
            WafThreshold = 0,
            UaEntropyThreshold = 0,
            UseIsolationForest = false
        };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var rules = new RollingThresholdDetector(Options.Create(settings), cache);
        var ml = new MlAnomalyDetector(Options.Create(settings));
        var hybrid = new HybridDetector(rules, ml);

        // warm-up for ml
        for (int i = 0; i < 3; i++)
            hybrid.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _);

        // event triggering rule
        Assert.True(hybrid.Observe(new RequestFeature("c", 1, 5, "/r", 200, false, true), out var reason));
        Assert.Equal("waf_spike", reason);
    }

    /// <summary>
    /// Verifies that enabling IsolationForest triggers a fail-fast exception since it is unsupported.
    /// </summary>
    [Fact]
    public void MlDetector_UseIsolationForest_Throws()
    {
        var settings = new AnomalyDetectionSettings
        {
            UseIsolationForest = true
        };

        Assert.Throws<NotImplementedException>(() => new MlAnomalyDetector(Options.Create(settings)));
    }

    /// <summary>
    /// Feeds a labeled dataset through MlAnomalyDetector with ML enabled
    /// and asserts it yields few false positives while capturing most true anomalies.
    /// </summary>
    [Fact]
    public void MlDetectorDetectsLabeledDataset()
    {
        var settings = new AnomalyDetectionSettings
        {
            Mode = DetectionMode.Ml,
            BaselineSampleSize = 80,
            TrainingWindowMinutes = 60,
            RetrainIntervalMinutes = 60,
            UseIsolationForest = false,
            UseMl = true
        };
        var ml = new MlAnomalyDetector(Options.Create(settings));

        for (int i = 0; i < 80; i++)
            ml.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _);

        int fp = 0;
        for (int i = 0; i < 80; i++)
            if (ml.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _))
                fp++;

        int tp = 0;
        for (int i = 0; i < 20; i++)
            if (ml.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out _))
                tp++;

        Assert.True(tp >= 15);
        Assert.True(fp <= 1);
    }

    /// <summary>
    /// Ensures the detector saves its model and threshold to disk and, after a new instance starts,
    /// immediately loads them (no warm-up) and keeps the same anomaly behavior.
    /// </summary>
    [Fact]
    public void MlDetector_PersistsAndReloads_ModelAndThreshold()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aegis_ml_test_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        var settings = new AnomalyDetectionSettings
        {
            BaselineSampleSize = 10,
            MinSamplesGuard = 5,
            TrainingWindowMinutes = 1,
            RetrainIntervalMinutes = 60,
            ScoreQuantile = 0.99,
            MinVarianceGuard = 999, // force fallback: Score = RPS
            UseIsolationForest = false,
            ModelPath = Path.Combine(dir, "model.zip"),
            ThresholdPath = Path.Combine(dir, "thr.txt")
        };

        var ml1 = new MlAnomalyDetector(Options.Create(settings));

        // Train baseline on "normal" traffic (RPS=1)
        for (int i = 0; i < 10; i++)
            ml1.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _);

        // Sanity: normal is not anomaly
        Assert.False(ml1.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _));
        // Clear spike should be anomaly
        Assert.True(ml1.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out _));

        // New instance: must load persisted model+threshold and be active immediately (no warm-up)
        var ml2 = new MlAnomalyDetector(Options.Create(settings));
        Assert.True(ml2.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out _));
        Assert.False(ml2.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _));

        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Confirms baseline training ignores “dirty” samples (e.g., WAF/SchemaError) so they don’t contaminate the model;
    /// once clean samples arrive, anomalies are detected as expected.
    /// </summary>
    /// <summary>
    /// Baseline must ignore dirty samples (WAF/SchemaError). With only dirty samples,
    /// the detector stays untrained; a spike should NOT be flagged. After feeding clean
    /// samples to reach BaselineSampleSize, the spike SHOULD be flagged.
    /// Uses isolated persistence paths to avoid cross-test interference.
    /// </summary>
    [Fact]
    public void MlDetector_ExcludesFlaggedSamples_FromBaseline()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aegis_ml_hygiene_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        try
        {
            var settings = new AnomalyDetectionSettings
            {
                BaselineSampleSize = 10,
                ScoreQuantile = 0.99,
                MinVarianceGuard = 999, // deterministic fallback if/when trained
                UseIsolationForest = false,
                ModelPath = Path.Combine(dir, "model.zip"),
                ThresholdPath = Path.Combine(dir, "thr.txt")
            };
            var ml = new MlAnomalyDetector(Options.Create(settings));

            // Feed ONLY dirty samples -> baseline MUST NOT complete
            for (int i = 0; i < 10; i++)
                ml.Observe(new RequestFeature("c", 1, 5, "/r", 200, SchemaError: true), out _);

            // Still untrained -> even a clear spike should return false
            Assert.False(ml.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out _));

            // Now feed clean samples to reach baseline
            for (int i = 0; i < settings.BaselineSampleSize; i++)
                ml.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _);

            // After baseline, spike should be detected
            Assert.True(ml.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out _));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Regression test ensuring constant non-zero features do not cause NaNs during baseline training.
    /// </summary>
    [Fact]
    public void MlDetector_ConstantFeature_DoesNotNaN()
    {
        var settings = new AnomalyDetectionSettings
        {
            BaselineSampleSize = 10,
            MinSamplesGuard = 5,
            TrainingWindowMinutes = 60,
            RetrainIntervalMinutes = 60,
            MinVarianceGuard = 1e-6,
            UseIsolationForest = false
        };

        var ml = new MlAnomalyDetector(Options.Create(settings));

        // Baseline where UaEntropy is constant but RPS and Method vary
        for (int i = 0; i < 10; i++)
        {
            var method = (i % 2 == 0) ? "GET" : "POST";
            ml.Observe(new RequestFeature("c", 1 + (i % 2), 7.0, "/r", 200, false, false, method), out _);
        }

        Assert.True(ml.Observe(new RequestFeature("c", 50, 7.0, "/r", 200, false), out var reason));
        Assert.Equal("ml_outlier", reason);
    }

    /// <summary>
    /// Validates that the chosen quantile sets the false-positive budget:
    /// with fallback scoring (Score = RPS), normals rarely exceed the threshold,
    /// while spikes (higher RPS) are flagged as anomalies.
    /// 
    /// Baseline: 100 samples alternating RPS {1,2}. Sorted, the 90th percentile (floor index) is 2.
    /// Thus threshold = 2; 1.5 < 2 (not anomalous), 3 > 2 (anomalous).
    /// </summary>
    [Fact]
    public void MlDetector_QuantileControlsFalsePositives_WithFallbackScoring()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aegis_ml_test_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        try
        {
            var settings = new AnomalyDetectionSettings
            {
                BaselineSampleSize = 100,
                ScoreQuantile = 0.90,      // 90th percentile -> threshold = 2 for {1,2} alternating
                MinVarianceGuard = 999,    // force fallback (Score = RPS)
                UseIsolationForest = false,
                ModelPath = Path.Combine(dir, "model.zip"),
                ThresholdPath = Path.Combine(dir, "thr.txt")
            };

            var ml = new MlAnomalyDetector(Options.Create(settings));

            // Baseline: alternate RPS 1 and 2 → threshold becomes 2
            for (int i = 0; i < 100; i++)
            {
                var rps = (i % 2 == 0) ? 1 : 2;
                ml.Observe(new RequestFeature("c", rps, 5, "/r", 200, false), out _);
            }

            // Normals around 1.5 → should not cross threshold=2
            int fp = 0;
            for (int i = 0; i < 100; i++)
            {
                if (ml.Observe(new RequestFeature("c", 1.5, 5, "/r", 200, false), out _))
                    fp++;
            }

            // Spikes at 3 → should cross threshold=2
            int tp = 0;
            for (int i = 0; i < 20; i++)
            {
                if (ml.Observe(new RequestFeature("c", 3, 5, "/r", 200, false), out _))
                    tp++;
            }

            Assert.Equal(0, fp);
            Assert.Equal(20, tp);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Ensures periodic retrain re-calibrates threshold upward when “normal” traffic drifts higher.
    /// </summary>
    [Fact]
    public void MlDetector_Retrain_RecalibratesThreshold()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aegis_ml_retrain_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        try
        {
            var settings = new AnomalyDetectionSettings
            {
                BaselineSampleSize = 30,
                MinSamplesGuard = 10,
                TrainingWindowMinutes = 60,
                RetrainIntervalMinutes = 60,
                MinVarianceGuard = 1e-6,
                ScoreQuantile = 0.95,
                UseIsolationForest = false,
                ModelPath = Path.Combine(dir, "model.zip"),
                ThresholdPath = Path.Combine(dir, "thr.txt")
            };

            var ml = new MlAnomalyDetector(Options.Create(settings));

            // Baseline: low RPS (1 or 2)
            for (int i = 0; i < 30; i++)
                ml.Observe(new RequestFeature("c", 1 + (i % 2), 3.0, "/r", 200, false), out _);

            var holder = typeof(MlAnomalyDetector).GetField("_holder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ml)!;
            var thrProp = holder.GetType().GetProperty("Threshold")!;
            var thrBefore = (double)thrProp.GetValue(holder)!;

            // New clean data with higher RPS (3 or 4)
            for (int i = 0; i < 50; i++)
                ml.Observe(new RequestFeature("c", 3 + (i % 2), 3.0, "/r", 200, false), out _);

            // Trigger retrain (invoke private method to avoid waiting for timer)
            var retrain = typeof(MlAnomalyDetector).GetMethod("RetrainAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((Task)retrain.Invoke(ml, Array.Empty<object>())!).GetAwaiter().GetResult();

            var thrAfter = (double)thrProp.GetValue(holder)!;
            Assert.True(thrAfter > thrBefore, "Expected threshold to increase after drift to higher RPS.");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// Ensures retraining prunes samples outside the training window before recalibration.
    /// </summary>
    [Fact]
    public void MlDetector_Retrain_PrunesOldSamples()
    {
        var settings = new AnomalyDetectionSettings
        {
            BaselineSampleSize = 5,
            MinSamplesGuard = 3,
            TrainingWindowMinutes = 1,
            RetrainIntervalMinutes = 60,
            MinVarianceGuard = 1e-6,
            UseIsolationForest = false
        };

        var ml = new MlAnomalyDetector(Options.Create(settings));

        // Baseline low RPS
        for (int i = 0; i < 5; i++)
            ml.Observe(new RequestFeature("c", 1, 3.0, "/r", 200, false), out _);

        var holder = typeof(MlAnomalyDetector).GetField("_holder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ml)!;
        var thrProp = holder.GetType().GetProperty("Threshold")!;
        var thrBefore = (double)thrProp.GetValue(holder)!;

        // Make baseline samples old
        var bufField = typeof(MlAnomalyDetector).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queue = (ConcurrentQueue<(DateTime ts, float[] vec)>)bufField.GetValue(ml)!;
        var arr = queue.ToArray();
        while (queue.TryDequeue(out _)) { }
        foreach (var e in arr)
            queue.Enqueue((DateTime.UtcNow - TimeSpan.FromMinutes(5), e.vec));

        // Add new high-RPS current samples
        for (int i = 0; i < 5; i++)
            ml.Observe(new RequestFeature("c", 10, 3.0, "/r", 200, false), out _);

        // Retrain and ensure old samples are pruned
        var retrain = typeof(MlAnomalyDetector).GetMethod("RetrainAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((Task)retrain.Invoke(ml, Array.Empty<object>())!).GetAwaiter().GetResult();

        var thrAfter = (double)thrProp.GetValue(holder)!;
        Assert.True(thrAfter > thrBefore, $"Expected threshold to increase, before={thrBefore}, after={thrAfter}");
    }

    /// <summary>
    /// Validates thread-safety: many concurrent Observe() calls use the pooled PredictionEngine without errors.
    /// </summary>
    [Fact]
    public void MlDetector_ConcurrentObserve_IsThreadSafe()
    {
        var settings = new AnomalyDetectionSettings
        {
            BaselineSampleSize = 40,
            MinSamplesGuard = 20,
            TrainingWindowMinutes = 60,
            RetrainIntervalMinutes = 60,
            MinVarianceGuard = 1e-6,
            ScoreQuantile = 0.99,
            UseIsolationForest = false
        };

        var ml = new MlAnomalyDetector(Options.Create(settings));

        // Baseline with variance
        for (int i = 0; i < 40; i++)
            ml.Observe(new RequestFeature("c", 1 + (i % 3), 3.0, "/r", 200, false), out _);

        int anomalies = 0;
        int total = 5000;
        var ex = new ConcurrentQueue<Exception>();

        Parallel.For(0, total, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            try
            {
                var rps = (i % 10 == 0) ? 50 : 1; // occasional spike
                if (ml.Observe(new RequestFeature("c", rps, 3.0, "/r", 200, false), out _))
                    Interlocked.Increment(ref anomalies);
            }
            catch (Exception e) { ex.Enqueue(e); }
        });

        Assert.True(ex.IsEmpty, $"No exceptions expected. First: {ex.FirstOrDefault()?.Message}");
        Assert.True(anomalies > 0, "Some spikes should be flagged under contention.");
        Assert.True(anomalies < total / 5, "Anomaly rate should be well below 20% for this mix.");
    }

    /// <summary>
    /// Confirms startup resilience: corrupt persisted model/threshold triggers warm-up then recovery to a working state.
    /// </summary>
    [Fact]
    public void MlDetector_LoadCorruptModel_FallsBackToWarmup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aegis_ml_corrupt_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        try
        {
            var modelPath = Path.Combine(dir, "model.zip");
            var thrPath = Path.Combine(dir, "thr.txt");

            File.WriteAllText(modelPath, "not-a-valid-mlnet-model");
            File.WriteAllText(thrPath, "NaN");

            var settings = new AnomalyDetectionSettings
            {
                BaselineSampleSize = 10,
                MinSamplesGuard = 5,
                TrainingWindowMinutes = 60,
                RetrainIntervalMinutes = 60,
                MinVarianceGuard = 999, // ensures fallback after warm-up
                UseIsolationForest = false,
                ModelPath = modelPath,
                ThresholdPath = thrPath
            };

            var ml = new MlAnomalyDetector(Options.Create(settings));

            // Initially not trained → even a spike isn't flagged
            Assert.False(ml.Observe(new RequestFeature("c", 50, 3.0, "/r", 200, false), out _));

            // Warm-up with clean samples enables detector
            for (int i = 0; i < 10; i++)
                ml.Observe(new RequestFeature("c", 1, 3.0, "/r", 200, false), out _);

            Assert.True(ml.Observe(new RequestFeature("c", 50, 3.0, "/r", 200, false), out _));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// Ensures in Hybrid mode that when rules are effectively disabled, ML still detects outliers.
    /// </summary>
    [Fact]
    public void Hybrid_MlFires_WhenRulesDont()
    {
        var settings = new AnomalyDetectionSettings
        {
            // Raise rule thresholds so rules never trigger in this test
            RpsThreshold = 10_000,
            FourXxThreshold = int.MaxValue,
            FiveXxThreshold = int.MaxValue,
            WafThreshold = int.MaxValue,
            UaEntropyThreshold = 0,

            Mode = DetectionMode.Hybrid,
            BaselineSampleSize = 20,
            MinVarianceGuard = 999, // deterministic fallback for this test
            UseIsolationForest = false
        };

        var cache = new MemoryCache(new MemoryCacheOptions());
        var rules = new RollingThresholdDetector(Options.Create(settings), cache);
        var ml = new MlAnomalyDetector(Options.Create(settings));
        var hybrid = new HybridDetector(rules, ml);

        // Warm-up for ML
        for (int i = 0; i < 20; i++)
            hybrid.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _);

        // High RPS should be caught by ML even though rules won't fire
        Assert.True(hybrid.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out var reason));
        Assert.Equal("ml_outlier", reason);
    }
}
