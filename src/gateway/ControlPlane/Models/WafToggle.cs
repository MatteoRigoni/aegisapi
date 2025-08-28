namespace Gateway.ControlPlane.Models;

public record WafToggle
{
    public string Rule { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}
