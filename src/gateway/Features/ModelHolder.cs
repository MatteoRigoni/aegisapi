using System.Threading;
using Microsoft.ML;

namespace Gateway.Features;

public class ModelHolder
{
    private PredictionEngine<AnomalyVector, AnomalyPrediction>? _engine;
    private double _threshold;

    public PredictionEngine<AnomalyVector, AnomalyPrediction>? Engine => Volatile.Read(ref _engine);
    public double Threshold => Volatile.Read(ref _threshold);

    public void Swap(PredictionEngine<AnomalyVector, AnomalyPrediction> engine, double threshold)
    {
        Interlocked.Exchange(ref _engine, engine);
        Interlocked.Exchange(ref _threshold, threshold);
    }
}

public class AnomalyVector
{
    [VectorType(4)]
    public float[] Features { get; set; } = Array.Empty<float>();
}

public class AnomalyPrediction
{
    public bool PredictedLabel { get; set; }
    public float Score { get; set; }
}
