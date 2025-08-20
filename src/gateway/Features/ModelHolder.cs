using System.Threading;
using Microsoft.ML;
using Microsoft.Extensions.ObjectPool;
using Microsoft.ML.Data;

namespace Gateway.Features;

public class ModelHolder
{
    private ObjectPool<PredictionEngine<AnomalyVector, AnomalyPrediction>>? _pool;
    private double _threshold;

    public ObjectPool<PredictionEngine<AnomalyVector, AnomalyPrediction>>? Pool => Volatile.Read(ref _pool);
    public double Threshold => Volatile.Read(ref _threshold);

    public void Swap(ObjectPool<PredictionEngine<AnomalyVector, AnomalyPrediction>>? pool, double threshold)
    {
        Interlocked.Exchange(ref _pool, pool);
        Interlocked.Exchange(ref _threshold, threshold);
    }
}

public class AnomalyVector
{
    [VectorType(6)]
    public float[] Features { get; set; } = Array.Empty<float>();
}

public class AnomalyPrediction
{
    public bool PredictedLabel { get; set; }
    public float Score { get; set; }
}
