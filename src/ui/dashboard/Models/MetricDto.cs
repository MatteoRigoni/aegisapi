namespace Dashboard.Models;

public record MetricDto(DateTime Timestamp, double Rps, double UaEntropy, int SchemaErrors, int WafBlocks);
