namespace Gateway.ControlPlane.Models;

public record SchemaRecord
{
    public string Path { get; init; } = "";
    public string Schema { get; init; } = "";
}
