namespace Dashboard.Models;

public record NetworkNodeDto(string Id, string Label, int Requests, double Latency);
public record NetworkLinkDto(string SourceId, string TargetId, int Requests, double Latency);
public record NetworkDto(IReadOnlyList<NetworkNodeDto> Nodes, IReadOnlyList<NetworkLinkDto> Links);
