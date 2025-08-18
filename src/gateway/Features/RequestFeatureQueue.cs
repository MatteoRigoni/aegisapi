using System.Threading.Channels;

namespace Gateway.Features;

public interface IRequestFeatureQueue
{
    void Enqueue(RequestFeature feature);
    IAsyncEnumerable<RequestFeature> DequeueAllAsync(CancellationToken token);
    void Seed(IEnumerable<RequestFeature> features);
}

public class RequestFeatureQueue : IRequestFeatureQueue
{
    private readonly Channel<RequestFeature> _channel = Channel.CreateUnbounded<RequestFeature>();

    public void Enqueue(RequestFeature feature)
    {
        _channel.Writer.TryWrite(feature);
    }

    public IAsyncEnumerable<RequestFeature> DequeueAllAsync(CancellationToken token) => _channel.Reader.ReadAllAsync(token);

    public void Seed(IEnumerable<RequestFeature> features)
    {
        foreach (var feature in features)
        {
            _channel.Writer.TryWrite(feature);
        }
    }
}
