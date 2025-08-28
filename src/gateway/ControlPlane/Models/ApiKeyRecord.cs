namespace Gateway.ControlPlane.Models;

public record ApiKeyRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Hash { get; init; } = string.Empty;
    public string Plan { get; init; } = string.Empty;
}
