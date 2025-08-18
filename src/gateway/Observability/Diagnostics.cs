using System.Diagnostics.Metrics;

namespace Gateway.Observability;

public static class GatewayDiagnostics
{
    public const string MeterName = "Gateway";

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> RateLimitHits =
        Meter.CreateCounter<long>("gateway.rate_limit_hits");

    public static readonly Counter<long> WafBlocks =
        Meter.CreateCounter<long>("gateway.waf_blocks");

    public static readonly Counter<long> SchemaValidationErrors =
        Meter.CreateCounter<long>("gateway.schema_validation_errors");
}
