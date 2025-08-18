using System.Text;
using System.Text.RegularExpressions;
using Gateway.Settings;
using Microsoft.Extensions.Options;

namespace Gateway.Security;

public sealed class WafMiddleware
{
    private readonly RequestDelegate _next;
    private readonly WafSettings _settings;
    private readonly ILogger<WafMiddleware> _logger;

    public WafMiddleware(RequestDelegate next, IOptions<WafSettings> options, ILogger<WafMiddleware> logger)
    {
        _next = next;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var reason = await CheckRequestAsync(context);
        if (reason is not null)
        {
            _logger.LogWarning("Request blocked by WAF: {Reason}", reason);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        await _next(context);
    }

    private async Task<string?> CheckRequestAsync(HttpContext context)
    {
        var toInspect = new List<string>
        {
            context.Request.Path.Value ?? string.Empty,
            context.Request.QueryString.Value ?? string.Empty
        };

        foreach (var q in context.Request.Query)
        {
            toInspect.Add(q.Key);
            toInspect.Add(q.Value);
        }

        if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            toInspect.Add(body);
        }

        foreach (var s in toInspect)
        {
            if (_settings.PathTraversal && s.Contains("../", StringComparison.Ordinal))
                return "PATH_TRAVERSAL";

            if (_settings.SqlInjection && Regex.IsMatch(s, @"(?i)(union.*select|select.*from|insert\s+into|drop\s+table|--|\bOR\b\s+\d=\d)"))
                return "SQLI";

            if (_settings.Xss && Regex.IsMatch(s, @"(?i)<script|onerror=|javascript:"))
                return "XSS";

            if (_settings.Ssrf && Regex.IsMatch(s, @"(?i)https?://(localhost|127\.0\.0\.1|0\.0\.0\.0|169\.254\.169\.254|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})"))
                return "SSRF";
        }

        return null;
    }
}
