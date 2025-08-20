using Gateway.Features;
using Microsoft.Extensions.Options;

namespace Gateway.IntegrationTests;

public class AnomalyDetectionTests
{
    [Fact]
    public void RulesDetectorDetectsSpikes()
    {
        var settings = new AnomalyDetectionSettings
        {
            RpsThreshold = 5,
            FourXxThreshold = 1,
            FiveXxThreshold = 0,
            WafThreshold = 0,
            UaEntropyThreshold = 0
        };
        var detector = new RollingThresholdDetector(Options.Create(settings));

        // produce 6 requests quickly to exceed RPS threshold (5 over 5s)
        for (int i = 0; i < 6; i++)
            detector.Observe(new RequestFeature("c", 0, 5, "/r", 200, false), out _);
        Assert.True(detector.Observe(new RequestFeature("c", 0, 5, "/r", 200, false), out var reason) && reason == "rps spike");

        detector.Observe(new RequestFeature("c", 0, 5, "/r", 404, false), out _);
        Assert.True(detector.Observe(new RequestFeature("c", 0, 5, "/r", 404, false), out reason) && reason == "4xx spike");

        Assert.True(detector.Observe(new RequestFeature("c", 0, 5, "/r", 500, false), out reason) && reason == "5xx spike");

        Assert.True(detector.Observe(new RequestFeature("c", 0, 5, "/r", 200, false, true), out reason) && reason == "waf spike");
    }

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
        Assert.True(ml.Observe(new RequestFeature("c", 50, 5, "/r", 200, false), out var reason) && reason == "ml anomaly");
    }

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
        var rules = new RollingThresholdDetector(Options.Create(settings));
        var ml = new MlAnomalyDetector(Options.Create(settings));
        var hybrid = new HybridDetector(rules, ml);

        // warm-up for ml
        for (int i = 0; i < 3; i++)
            hybrid.Observe(new RequestFeature("c", 1, 5, "/r", 200, false), out _);

        // event triggering rule
        Assert.True(hybrid.Observe(new RequestFeature("c", 1, 5, "/r", 200, false, true), out var reason));
        Assert.Equal("waf spike", reason);
    }
}
