using Gateway.AI;
using Gateway.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Features;

public sealed class FeatureConsumerService : BackgroundService
{
    private readonly AnomalyDetectionService _anomalyService;
    private readonly ISummarizerClient _summarizer;
    private readonly ILogger<FeatureConsumerService> _logger;
    private readonly IncidentBundleFactory _bundleFactory = new();

    public FeatureConsumerService(AnomalyDetectionService anomalyService, ISummarizerClient summarizer, ILogger<FeatureConsumerService> logger)
    {
        _anomalyService = anomalyService;
        _summarizer = summarizer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (_anomalyService.Anomalies.TryTake(out var anomaly))
            {
                var ev = anomaly.feature;
                var fe = new FeatureEventLite(DateTimeOffset.UtcNow, ev.ClientId ?? string.Empty, ev.RouteKey, ev.Status, ev.SchemaError, ev.WafHit ? 1 : 0, ev.UaEntropy);
                _bundleFactory.Add(fe);
                var bundle = _bundleFactory.Create("dev", anomaly.reason);
                try
                {
                    var summary = await _summarizer.SummarizeAsync(bundle, stoppingToken);
                    _logger.LogWarning("Incident: {Summary} (patch: {Patch})", summary?.Summary, summary?.SuggestedPolicyPatch?.Rule);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Summarizer call failed");
                }
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
