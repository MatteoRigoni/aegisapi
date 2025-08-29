namespace Dashboard.Models;

public record NetworkNodeDto(string Id, string Label, double Latency, int Requests);

public record NetworkEdgeDto(string SourceId, string TargetId, int Requests, double Latency);

public record NetworkGraphDto(IEnumerable<NetworkNodeDto> Nodes, IEnumerable<NetworkEdgeDto> Edges);
