using System;
using System.Text;
using System.Text.RegularExpressions;
using Gateway.ControlPlane.Stores;
using Gateway.Observability;
using System.Collections.Generic;
using System.Linq;

namespace Gateway.Security;

public sealed class WafMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWafToggleStore _store;
    private readonly ILogger<WafMiddleware> _logger;
    private IReadOnlyDictionary<string, bool> _toggles;

    private static readonly Regex SqlRegex = new(
        @"(?i)(union.*select|select.*from|insert\s+into|drop\s+table|--|\bOR\b\s+\d=\d)",
        RegexOptions.Compiled);
    private static readonly Regex XssRegex = new(
        @"(?i)<script|onerror=|javascript:",
        RegexOptions.Compiled);
    private static readonly Regex SsrfRegex = new(
        @"(?i)https?://(localhost|127\.0\.0\.1|0\.0\.0\.0|169\.254\.169\.254|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})",
        RegexOptions.Compiled);

    public WafMiddleware(RequestDelegate next, IWafToggleStore store, ILogger<WafMiddleware> logger)
    {
        _next = next;
        _store = store;
        _logger = logger;
        _toggles = BuildToggleMap();
        _store.Changed += () => _toggles = BuildToggleMap();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/cp", out _))
        {
            await _next(context);
            return;
        }

        var reason = await CheckRequestAsync(context);
        if (reason is not null)
        {
            _logger.LogWarning("Request blocked by WAF: {Reason}", reason);
            GatewayDiagnostics.WafBlocks.Add(1);
            context.Items["WafHit"] = true;
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
            Uri.UnescapeDataString(context.Request.Path.Value ?? string.Empty),
            Uri.UnescapeDataString(context.Request.QueryString.Value ?? string.Empty)
        };

        foreach (var q in context.Request.Query)
        {
            toInspect.Add(Uri.UnescapeDataString(q.Key ?? string.Empty));
            toInspect.Add(Uri.UnescapeDataString(q.Value.ToString() ?? string.Empty));
        }

        if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            toInspect.Add(body);
        }

        var toggles = _toggles;
        foreach (var s in toInspect)
        {
            if (IsEnabled(toggles, "PathTraversal") && s.Contains("../", StringComparison.Ordinal))
                return "PATH_TRAVERSAL";

            if (IsEnabled(toggles, "SqlInjection") && SqlRegex.IsMatch(s))
                return "SQLI";

            if (IsEnabled(toggles, "Xss") && XssRegex.IsMatch(s))
                return "XSS";

            if (IsEnabled(toggles, "Ssrf") && SsrfRegex.IsMatch(s))
                return "SSRF";
        }

        return null;
    }

    private IReadOnlyDictionary<string, bool> BuildToggleMap()
        => _store.GetAll().ToDictionary(t => t.Rule, t => t.Enabled, StringComparer.OrdinalIgnoreCase);

    private static bool IsEnabled(IReadOnlyDictionary<string, bool> toggles, string rule)
        => !toggles.TryGetValue(rule, out var enabled) || enabled;
}
