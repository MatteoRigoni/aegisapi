using Microsoft.Extensions.Configuration;
namespace Summarizer.Llm;

public sealed class AzureOpenAiLlmClient : ILlmClient
{
    private readonly IConfiguration _cfg;
    public AzureOpenAiLlmClient(IConfiguration cfg) => _cfg = cfg;

    public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        // Placeholder for real Azure OpenAI integration
        return Task.FromResult("{}");
    }
}
