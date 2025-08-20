namespace Gateway.Features;

public class HybridDetector : IAnomalyDetector
{
    private readonly RollingThresholdDetector _rules;
    private readonly MlAnomalyDetector _ml;

    public HybridDetector(RollingThresholdDetector rules, MlAnomalyDetector ml)
    {
        _rules = rules;
        _ml = ml;
    }

    public bool Observe(RequestFeature feature, out string reason)
    {
        if (_rules.Observe(feature, out reason))
            return true;
        return _ml.Observe(feature, out reason);
    }
}
