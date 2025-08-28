namespace Summarizer.Llm;
public sealed class FakeLlmClient : ILlmClient
{
    public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        var json = """
        {"summary":"Fake summary","probable_cause":"Spike from single client","suggested_policy_patch":{"type":"RateLimit","path":"/api/orders","rule":"token-bucket","params":{"rpm":"100"}},"confidence":0.72,"next_steps":["Monitor","Apply canary limit"]}
        """;
        return Task.FromResult(json);
    }
}
