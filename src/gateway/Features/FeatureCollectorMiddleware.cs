using Microsoft.AspNetCore.Http;
using System.Linq;

namespace Gateway.Features;

public class FeatureCollectorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRequestFeatureQueue _queue;

    public FeatureCollectorMiddleware(RequestDelegate next, IRequestFeatureQueue queue)
    {
        _next = next;
        _queue = queue;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var clientId = context.Items.TryGetValue("ClientId", out var cId) ? cId as string : null;
        var rpsWindow = context.Items.TryGetValue("RpsWindow", out var rps) ? Convert.ToDouble(rps) : 0d;
        var ua = context.Request.Headers.UserAgent.ToString();
        var uaEntropy = CalculateEntropy(ua);
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;
        var routeKey = NormalizeRoute(path);
        var status = context.Response.StatusCode;
        var schemaError = context.Items.ContainsKey("SchemaError");
        var wafHit = context.Items.ContainsKey("WafHit");

        var feature = new RequestFeature(clientId, rpsWindow, uaEntropy, path, status, schemaError, wafHit, method, routeKey);
        _queue.Enqueue(feature);
    }

    private static double CalculateEntropy(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        var groups = input.GroupBy(c => c);
        var entropy = 0d;
        foreach (var g in groups)
        {
            var p = (double)g.Count() / input.Length;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    private static string NormalizeRoute(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return "/";
        return "/" + string.Join('/', segments.Take(2));
    }
}
