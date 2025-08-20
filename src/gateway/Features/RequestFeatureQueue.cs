using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Gateway.Features;

public interface IFeatureSource
{
    IAsyncEnumerable<RequestFeature> DequeueAllAsync(CancellationToken token);
}

public interface IRequestFeatureQueue : IFeatureSource
{
    void Enqueue(RequestFeature feature);
    void Seed(IEnumerable<RequestFeature> features);
}

public class RequestFeatureQueue : IRequestFeatureQueue
{
    private readonly Channel<RequestFeature> _channel;

    public RequestFeatureQueue(IOptions<AnomalyDetectionSettings> options)
    {
        var capacity = Math.Max(1, options.Value.FeatureQueueCapacity);
        var opts = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _channel = Channel.CreateBounded<RequestFeature>(opts);
    }

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
