using Summarizer.Model;
using Summarizer.Llm;
using Summarizer.Security;
using System.Text.Json;

public sealed class SummarizerService
{
    private readonly ILlmClient _llm;
    public SummarizerService(ILlmClient llm) => _llm = llm;

    public async Task<SummaryResponse> SummarizeAsync(IncidentBundle bundle, CancellationToken ct = default)
    {
        var compact = JsonSerializer.Serialize(bundle);
        var redacted = Redactor.Mask(compact);

        var prompt = $$"""
You are a security incident analyst. Summarize the API gateway anomaly.

STRICT RULES:
- Never echo secrets, tokens, emails, or raw IPs. If present, they are masked.
- Return ONLY valid JSON for this schema:
  {
    "summary": string,
    "probable_cause": string,
    "suggested_policy_patch": {
      "type": "RateLimit|WafRule|SchemaValidation",
      "path": string,
      "rule": string,
      "params": { "string": "string" }
    } | null,
    "confidence": number,
    "next_steps": [string]
  }

Context (redacted):
{{redacted}}
""";

        var raw = await _llm.CompleteAsync(prompt, ct);

        SummaryResponse? parsed = null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            PolicyPatch? patch = null;
            if (root.TryGetProperty("suggested_policy_patch", out var pp) && pp.ValueKind == JsonValueKind.Object)
            {
                var type = pp.GetProperty("type").GetString() ?? "RateLimit";
                var path = pp.GetProperty("path").GetString() ?? "/api";
                var rule = pp.GetProperty("rule").GetString() ?? "token-bucket";
                var dict = new Dictionary<string,string>();
                if (pp.TryGetProperty("params", out var prm) && prm.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in prm.EnumerateObject()) dict[p.Name] = p.Value.GetString() ?? "";
                }
                patch = new PolicyPatch(type, path, rule, dict);
            }

            parsed = new SummaryResponse(
                root.GetProperty("summary").GetString() ?? "N/A",
                root.GetProperty("probable_cause").GetString() ?? "N/A",
                patch,
                root.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5,
                root.TryGetProperty("next_steps", out var ns) && ns.ValueKind == JsonValueKind.Array
                    ? ns.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                    : Array.Empty<string>()
            );
        }
        catch
        {
            parsed = new SummaryResponse("Invalid LLM output", "Parse error", null, 0.0,
                new[] { "Fallback to rules", "Check logs" });
        }

        return parsed!;
    }
}
