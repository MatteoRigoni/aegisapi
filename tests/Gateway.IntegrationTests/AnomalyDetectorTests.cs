using Gateway.Features;
using Microsoft.Extensions.Options;

namespace Gateway.IntegrationTests;

public class AnomalyDetectorTests
{
    [Fact]
    public async Task DetectsExpectedAnomalies()
    {
        var queue = new RequestFeatureQueue();
        var settings = Options.Create(new AnomalyDetectionSettings
        {
            WindowSeconds = 1,
            RpsThreshold = 3,
            FourXxThreshold = 1,
            FiveXxThreshold = 0,
            WafThreshold = 0,
            UseMl = false
        });
        var detector = new AnomalyDetector(queue, settings);
        await detector.StartAsync(CancellationToken.None);

        // Normal traffic
        queue.Enqueue(new RequestFeature("c1", 0, 0, "/r", 200, false));
        queue.Enqueue(new RequestFeature("c1", 0, 0, "/r", 200, false));
        queue.Enqueue(new RequestFeature("c1", 0, 0, "/r", 404, false));
        queue.Enqueue(new RequestFeature("c1", 0, 0, "/r", 404, false)); // triggers 4xx and rps

        await Task.Delay(1100); // allow window to reset
        queue.Enqueue(new RequestFeature("c1", 0, 0, "/r", 500, false)); // triggers 5xx

        await Task.Delay(1100);
        queue.Enqueue(new RequestFeature("c1", 0, 0, "/r", 200, false, true)); // triggers WAF

        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();
        while (detector.Anomalies.Count < 3 && sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(50);
        }

        Assert.Equal(3, detector.Anomalies.Count);
        await detector.StopAsync(CancellationToken.None);
    }
}
