namespace Gateway.Settings;

public class RateLimitingSettings
{
    public int DefaultRpm { get; set; } = 100;
    public Dictionary<string, int> Plans { get; set; } = new();

    public int GetLimit(string? plan)
    {
        if (!string.IsNullOrEmpty(plan) && Plans.TryGetValue(plan, out var limit))
            return limit;
        return DefaultRpm;
    }
}
