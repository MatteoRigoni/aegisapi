namespace Dashboard.Services.Mock;

public class MockControlPlaneService : IControlPlaneService
{
    private string _policy = "{}";

    public Task ApplyFixAsync(string incidentId) => Task.CompletedTask;

    public Task<string> LoadPolicyAsync() => Task.FromResult(_policy);

    public Task SavePolicyAsync(string policyJson)
    {
        _policy = policyJson;
        return Task.CompletedTask;
    }
}
