using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Gateway.Security;
using Gateway.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Gateway.RateLimiting;

public sealed class ClientRateLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly RateLimitingSettings _settings;

    public ClientRateLimiterMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IOptions<RateLimitingSettings> options)
    {
        _next = next;
        _cache = cache;
        _settings = options.Value;
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
        var limit = _settings.GetLimit(plan);
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
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = Math.Ceiling(retryAfter).ToString(CultureInfo.InvariantCulture);
            return;
        }

        await _next(context);
    }

    private static string? GetClientId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value;
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
