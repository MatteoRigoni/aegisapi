namespace Summarizer.Model;

public sealed record FeatureEventLite(
    DateTimeOffset Timestamp,
    string ClientId,
    string RouteKey,
    int StatusCode,
    bool SchemaError,
    int WafHitCount,
    double UaEntropy);

public sealed record IncidentBundle(
    string Environment,
    string DetectorMode,
    string DetectorReason,
    IReadOnlyList<FeatureEventLite> RecentEvents,
    IDictionary<string,double> Counters,
    IDictionary<string,int> TopPaths,
    string? Notes);
