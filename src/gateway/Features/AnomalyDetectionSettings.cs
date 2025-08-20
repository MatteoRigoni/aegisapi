namespace Gateway.Features;

public class AnomalyDetectionSettings
{
    public int WindowSeconds { get; set; } = 1; // size of rolling window
    public double RpsThreshold { get; set; } = 100;
    public int FourXxThreshold { get; set; } = 20;
    public int FiveXxThreshold { get; set; } = 5;
    public int WafThreshold { get; set; } = 0;
    public bool UseMl { get; set; } = false;
    public bool UseIsolationForest { get; set; } = false;
    public bool UseZScore { get; set; } = false;
    public double ZScoreK { get; set; } = 3.0;
    public int FeatureQueueCapacity { get; set; } = 1000;
    public int PruneIntervalSeconds { get; set; } = 60;
}
