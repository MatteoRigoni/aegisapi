namespace Gateway.Settings;

public class ResilienceSettings
{
    public TimeoutSettings Timeout { get; set; } = new();
    public RetrySettings Retry { get; set; } = new();
    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();
}

public class TimeoutSettings
{
    public int DurationSeconds { get; set; } = 2;
}

public class RetrySettings
{
    public int Count { get; set; } = 2;
    public int BaseDelayMs { get; set; } = 100;
}

public class CircuitBreakerSettings
{
    public int FailureThreshold { get; set; } = 5;
    public int BreakDurationSeconds { get; set; } = 5;
}
