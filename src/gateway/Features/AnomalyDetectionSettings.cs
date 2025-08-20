namespace Gateway.Features;

public enum DetectionMode
{
    Rules,
    Ml,
    Hybrid
}

public class AnomalyDetectionSettings
{
    // Rolling threshold settings
    public DetectionMode Mode { get; set; } = DetectionMode.Rules;
    public int RpsWindowSeconds { get; set; } = 5;
    public int ErrorWindowSeconds { get; set; } = 60;
    public double RpsThreshold { get; set; } = 100; // requests per second
    public int FourXxThreshold { get; set; } = 20;
    public int FiveXxThreshold { get; set; } = 5;
    public int WafThreshold { get; set; } = 0;
    public double UaEntropyThreshold { get; set; } = 3.5;
    public int WindowTtlMinutes { get; set; } = 30;

    // Queue configuration
    public int FeatureQueueCapacity { get; set; } = 1000;

    // ML settings
    public bool UseMl { get; set; } = false;
    public bool UseIsolationForest { get; set; } = true;
    public int BaselineSampleSize { get; set; } = 100;
    public int TrainingWindowMinutes { get; set; } = 60;
    public int RetrainIntervalMinutes { get; set; } = 10;
    public double ScoreQuantile { get; set; } = 0.995;
    public int MinSamplesGuard { get; set; } = 50;
    public double MinVarianceGuard { get; set; } = 0.01;
    public string ModelPath { get; set; } = "anomaly_model.zip";
    public string ThresholdPath { get; set; } = "anomaly_threshold.json";
}
