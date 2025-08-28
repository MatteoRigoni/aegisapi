using Gateway.Security;
using Gateway.Observability;
using Gateway.ControlPlane.Stores;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Gateway.RateLimiting;

public sealed class ClientRateLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly IRateLimitPlanStore _plans;

    public ClientRateLimiterMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IRateLimitPlanStore plans)
    {
        _next = next;
        _cache = cache;
        _plans = plans;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientId(context);
        if (clientId is null)
        {
            await _next(context);
            return;
        }

        var plan = context.User.FindFirst("plan")?.Value;
        var defaultRpm = _plans.Get(IRateLimitPlanStore.DefaultPlan)?.plan.Rpm ?? 100;
        var limit = _plans.Get(plan ?? string.Empty)?.plan.Rpm ?? defaultRpm;
        context.Items["ClientId"] = clientId;
        context.Items["RpsWindow"] = limit / 60d;
        var now = DateTime.UtcNow;

        var bucket = _cache.GetOrCreate(clientId, e =>
        {
            e.SlidingExpiration = TimeSpan.FromMinutes(5);
            return new TokenBucket(limit, now);
        });
        double retryAfter;
        var allowed = false;
        lock (bucket!)
        {
            allowed = bucket!.TryConsume(limit, now, out retryAfter);
        }

        if (!allowed)
        {
            GatewayDiagnostics.RateLimitHits.Add(1);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = Math.Ceiling(retryAfter).ToString(CultureInfo.InvariantCulture);
            return;
        }

        await _next(context);
    }

    private static string? GetClientId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(sub))
            return sub;

        if (context.Request.Headers.TryGetValue(ApiKeyAuthenticationHandler.HeaderName, out var apiKeyValues))
        {
            var apiKey = apiKeyValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(apiKey))
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
                return Convert.ToHexString(hash);
            }
        }

        return null;
    }

    private sealed class TokenBucket
    {
        private double _tokens;
        private DateTime _lastRefill;

        public TokenBucket(int capacity, DateTime now)
        {
            _tokens = capacity;
            _lastRefill = now;
        }

        public bool TryConsume(int capacity, DateTime now, out double retryAfter)
        {
            var ratePerSec = capacity / 60d;
            var delta = (now - _lastRefill).TotalSeconds * ratePerSec;
            _tokens = Math.Min(capacity, _tokens + delta);
            _lastRefill = now;

            if (_tokens >= 1)
            {
                _tokens -= 1;
                retryAfter = 0;
                return true;
            }

            retryAfter = (1 - _tokens) / ratePerSec;
            return false;
        }
    }
}
