namespace Summarizer.Llm;
public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, CancellationToken ct = default);
}
