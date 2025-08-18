namespace Gateway.Features;

public record RequestFeature(
    string? ClientId,
    double RpsWindow,
    double UaEntropy,
    string Path,
    int Status,
    bool SchemaError);
