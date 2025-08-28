using Summarizer.Llm;
using Summarizer.Model;
using System.Text.RegularExpressions;

namespace Summarizer.UnitTests;

public class CapturingLlmClient : ILlmClient
{
    public string? Prompt { get; private set; }
    public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        Prompt = prompt;
        return Task.FromResult("{\"summary\":\"ok\",\"probable_cause\":\"x\",\"suggested_policy_patch\":null,\"confidence\":0.5,\"next_steps\":[]}");
    }
}

public class SummarizerServiceTests
{
    [Fact]
    public async Task SummarizeAsync_BuildsRedactedPrompt()
    {
        var llm = new CapturingLlmClient();
        var svc = new SummarizerService(llm);
        var bundle = new IncidentBundle("dev","Rules","reason", new List<FeatureEventLite>(), new Dictionary<string,double>{{"api_key",1}}, new Dictionary<string,int>(), "email@test.com 10.0.0.1 Bearer token");
        await svc.SummarizeAsync(bundle);
        Assert.NotNull(llm.Prompt);
        Assert.DoesNotContain("email@test.com", llm.Prompt);
        Assert.DoesNotContain("10.0.0.1", llm.Prompt);
        // Extract only the redacted context for assertion
        var match = Regex.Match(llm.Prompt!, @"Context \(redacted\):\s*(.*)", RegexOptions.Singleline);
        var context = match.Success ? match.Groups[1].Value : llm.Prompt!;
        Assert.DoesNotContain("token", context);
        Assert.Contains("Context (redacted):", llm.Prompt);
    }
}
