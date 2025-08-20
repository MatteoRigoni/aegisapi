using Gateway.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
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
        detector.Observe(new RequestFeature("c", 0, 5, "/r1", 200, false), out _);
        Assert.True(detector.Observe(new RequestFeature("c", 0, 5, "/r1", 200, false), out var reason) && reason == "rps_spike");

        // Same client but different route should not be flagged
        Assert.False(detector.Observe(new RequestFeature("c", 0, 5, "/r2", 200, false), out _));
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
    [Fact]
    public void MlDetector_ExcludesFlaggedSamples_FromBaseline()
    {
        var settings = new AnomalyDetectionSettings
        {
            BaselineSampleSize = 10,
            ScoreQuantile = 0.99,
            MinVarianceGuard = 999, // fallback path for determinism
            UseIsolationForest = false
        };
        var ml = new MlAnomalyDetector(Options.Create(settings));

        // Feed only flagged samples (schema errors) -> baseline MUST NOT complete
        for (int i = 0; i < 10; i++)
            ml.Observe(new RequestFeature("c", 1, 5, "/r", 200, SchemaError: true), out _);

        // Not trained yet -> even a clear spike should return false (no decision)
        Assert.False(ml.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out _));

        // Now feed clean samples to meet baseline
        for (int i = 0; i < 10; i++)
            ml.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _);

        // After baseline, spike should be detected
        Assert.True(ml.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out _));
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
}
