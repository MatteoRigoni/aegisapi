using Gateway.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
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

        // produce 6 requests quickly to exceed RPS threshold (5 over 5s)
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
            RetrainIntervalMinutes = 60
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
            UaEntropyThreshold = 0
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
}
