using Gateway.Settings;
using Microsoft.Extensions.Http.Resilience;    // ResilienceHandler
using Microsoft.Extensions.Options;
using Polly;                                   // Outcome<T>
using Polly.CircuitBreaker;                    // CircuitBreakerStrategyOptions<T>
using Polly.Retry;                             // RetryStrategyOptions<T>
using Polly.Timeout;                           // TimeoutRejectedException
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
        // ---- ShouldHandle comune per retry/breaker
        static bool IsTransient(Outcome<HttpResponseMessage> outcome)
        {
            if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
                return true;

            if (outcome.Result is { } r)
            {
                var code = (int)r.StatusCode;
                return code >= 500 || r.StatusCode == HttpStatusCode.RequestTimeout; // 5xx/408
            }

            return false;
        }

        var shouldHandleRetry = new Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>>(
            args => new ValueTask<bool>(IsTransient(args.Outcome)));

        var shouldHandleBreaker = new Func<CircuitBreakerPredicateArguments<HttpResponseMessage>, ValueTask<bool>>(
            args => new ValueTask<bool>(IsTransient(args.Outcome)));

        // ---- Timeout per tentativo (usare il type di Polly, non quello di Microsoft.Extensions.Resilience)
        var timeout = new Polly.Timeout.TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(_settings.Timeout.DurationSeconds)
        };

        // ---- Retry con backoff esponenziale + jitter
        var retry = new RetryStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = shouldHandleRetry,
            MaxRetryAttempts = _settings.Retry.Count,
            Delay = TimeSpan.FromMilliseconds(_settings.Retry.BaseDelayMs),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = args =>
            {
                _logger.LogWarning("Retry {Attempt} after {Delay} due to {Reason}",
                    args.AttemptNumber,
                    args.RetryDelay,
                    args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                return default;
            }
        };

        // ---- Circuit breaker
        var breaker = new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = shouldHandleBreaker,
            FailureRatio = 0.5,                               // 50% failure rate nella finestra
            MinimumThroughput = 10,                           // min N richieste valutate
            BreakDuration = TimeSpan.FromSeconds(_settings.CircuitBreaker.BreakDurationSeconds),
            OnOpened = _ => { _logger.LogError("Circuit OPEN for {Seconds}s", _settings.CircuitBreaker.BreakDurationSeconds); return default; },
            OnClosed = _ => { _logger.LogInformation("Circuit CLOSED"); return default; },
            OnHalfOpened = _ => { _logger.LogInformation("Circuit HALF-OPEN"); return default; }
        };

        // Ordine: Breaker (outer) -> Retry -> Timeout (inner per tentativo)
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(breaker)
            .AddRetry(retry)
            .AddTimeout(timeout)
            .Build();

#pragma warning disable EXTEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates
        return new ResilienceHandler(pipeline) { InnerHandler = innerHandler };
#pragma warning restore EXTEXP0001
    }
}
