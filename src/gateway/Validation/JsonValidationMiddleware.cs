using Json.Schema;
using System.Text.Json.Nodes;
using Gateway.Observability;

namespace Gateway.Validation;

public class JsonValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _schemaRoot;

    public JsonValidationMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _schemaRoot = Path.Combine(env.ContentRootPath, "Schemas");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", out var remainder))
        {
            await _next(context);
            return;
        }

        JsonSchema? schema = null;
        if (context.Request.ContentType?.Contains("application/json") == true)
        {
            var schemaFile = remainder.Value != null
                ? Path.Combine(_schemaRoot, remainder.Value.Trim('/') + ".json")
                : null;

            var validationResult = await TryValidateRequestAsync(context, schemaFile);
            if (!validationResult.isValid)
            {
                return;
            }
            schema = validationResult.schema;
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

    private async Task<(bool isValid, JsonSchema? schema)> TryValidateRequestAsync(HttpContext context, string? schemaFile)
    {
        JsonSchema? schema = null;
        if (schemaFile != null && File.Exists(schemaFile))
        {
            schema = JsonSchema.FromText(await File.ReadAllTextAsync(schemaFile));
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
                return (false, schema);
            }
        }
        return (true, schema);
    }
}
