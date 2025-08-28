namespace Gateway.ControlPlane.Models;

public record RouteConfig
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Path { get; init; } = "/";
    public string Destination { get; init; } = "";
    public string? AuthorizationPolicy { get; init; }
    public string? PathRemovePrefix { get; init; }
    public TimeSpan? ActivityTimeout { get; init; }
}
