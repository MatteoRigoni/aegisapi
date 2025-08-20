namespace Gateway.Features;

public record RequestFeature(
    string? ClientId,
    double RpsWindow,
    double UaEntropy,
    string Path,
    int Status,
    bool SchemaError,
    bool WafHit = false,
    string Method = "GET",
    string RouteKey = "");
