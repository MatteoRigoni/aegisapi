namespace Gateway.Features;

public interface IAnomalyDetector
{
    bool Observe(RequestFeature feature, out string reason);
}
