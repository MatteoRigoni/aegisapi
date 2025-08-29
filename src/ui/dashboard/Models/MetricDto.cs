namespace Dashboard.Models;

public record MetricDto(double Rps, double UaEntropy, int SchemaErrors, int WafBlocks);
