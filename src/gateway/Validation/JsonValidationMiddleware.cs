using Json.Schema;
using System.Text.Json.Nodes;
using Gateway.Observability;
using Gateway.ControlPlane.Stores;

namespace Gateway.Validation;

public class JsonValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISchemaStore _schemas;

    public JsonValidationMiddleware(RequestDelegate next, ISchemaStore schemas)
    {
        _next = next;
        _schemas = schemas;
    }

    public async Task Invoke(HttpContext context)
    {
        JsonSchema? schema = null;

        if (context.Request.Path.StartsWithSegments("/api", out var remainder) &&
            context.Request.ContentType?.Contains("application/json") == true)
        {
            var key = remainder.Value?.Trim('/');
            if (!string.IsNullOrEmpty(key))
            {
                var entry = _schemas.Get(key);
                if (entry.HasValue)
                {
                    schema = JsonSchema.FromText(entry.Value.schema.Schema);
                    context.Request.EnableBuffering();
                    using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                    var json = JsonNode.Parse(body);
                    var result = schema.Evaluate(json, new EvaluationOptions { OutputFormat = OutputFormat.List });
                    if (!result.IsValid)
                    {
                        GatewayDiagnostics.SchemaValidationErrors.Add(1);
                        context.Items["SchemaError"] = true;
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        var errors = result.Details
                           .Where(d => d.Errors != null && d.Errors.Count > 0)
                           .SelectMany(d => d.Errors!, (d, err) => new { path = d.InstanceLocation.ToString(), error = err.Value });
                        await context.Response.WriteAsJsonAsync(new { errors });
                        return;
                    }
                }
            }
        }

        if (schema != null)
        {
            var originalBody = context.Response.Body;
            await using var memStream = new MemoryStream();
            context.Response.Body = memStream;

            await _next(context);

            memStream.Seek(0, SeekOrigin.Begin);
            if (context.Response.ContentType?.Contains("application/json") == true)
            {
                var responseText = await new StreamReader(memStream).ReadToEndAsync();
                memStream.Seek(0, SeekOrigin.Begin);
                var json = JsonNode.Parse(responseText);
                var result = schema.Evaluate(json, new EvaluationOptions { OutputFormat = OutputFormat.List });
                if (!result.IsValid)
                {
                    GatewayDiagnostics.SchemaValidationErrors.Add(1);
                    context.Items["SchemaError"] = true;
                    context.Response.Body = originalBody;
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    var errors = result.Details
                       .Where(d => d.Errors != null && d.Errors.Count > 0)
                       .SelectMany(d => d.Errors!, (d, err) => new { path = d.InstanceLocation.ToString(), error = err.Value });
                    await context.Response.WriteAsJsonAsync(new { errors });
                    return;
                }
            }

            await memStream.CopyToAsync(originalBody);
            context.Response.Body = originalBody;
            return;
        }

        await _next(context);
    }
}
