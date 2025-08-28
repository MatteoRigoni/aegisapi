namespace Gateway.ControlPlane.Models;

public record RateLimitPlan
{
    public string Plan { get; init; } = "default";
    public int Rpm { get; init; } = 60;
}
