namespace Dashboard.Services;

public class MockPolicyService : IPolicyService
{
    private string _policy = "{\n  \"rules\": []\n}";

    public Task<string> GetPolicyAsync() => Task.FromResult(_policy);

    public Task SavePolicyAsync(string json)
    {
        _policy = json;
        return Task.CompletedTask;
    }
}
