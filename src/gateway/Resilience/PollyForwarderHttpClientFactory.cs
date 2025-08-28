using Gateway.Settings;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace Gateway.Resilience;

public sealed class ResilienceForwarderHttpClientFactory : ForwarderHttpClientFactory
{
    private readonly ResilienceSettings _settings;
    private readonly ILogger<ResilienceForwarderHttpClientFactory> _logger;

    public ResilienceForwarderHttpClientFactory(
        IOptions<ResilienceSettings> options,
        ILogger<ResilienceForwarderHttpClientFactory> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler innerHandler)
    {
        // Timeout per singolo tentativo
        var timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(_settings.Timeout.DurationSeconds));

        // Retry con backoff esponenziale e jitter
        var retry = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                _settings.Retry.Count,
                attempt => TimeSpan.FromMilliseconds(_settings.Retry.BaseDelayMs * Math.Pow(2, attempt - 1)),
                (outcome, delay, attempt, _) =>
                    _logger.LogWarning(
                        "Retry {Attempt} after {Delay} due to {Reason}",
                        attempt,
                        delay,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()));

        // Circuit breaker dopo N errori consecutivi
        var breaker = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                _settings.CircuitBreaker.FailureThreshold,
                TimeSpan.FromSeconds(_settings.CircuitBreaker.BreakDurationSeconds),
                (outcome, ts) => _logger.LogError("Circuit OPEN for {Seconds}s", ts.TotalSeconds),
                () => _logger.LogInformation("Circuit CLOSED"),
                () => _logger.LogInformation("Circuit HALF-OPEN"));

        var policy = Policy.WrapAsync(breaker, retry, timeout);

        return new PolicyHttpMessageHandler(policy) { InnerHandler = innerHandler };
    }
}

