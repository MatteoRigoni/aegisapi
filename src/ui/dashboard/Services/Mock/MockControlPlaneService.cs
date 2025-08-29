namespace Dashboard.Services.Mock;

public class MockControlPlaneService : IControlPlaneService
{
    private string _policy = @"{
  \"rules\": [
    { \"id\": 1, \"action\": \"allow\", \"condition\": \"user.role == 'admin'\" },
    { \"id\": 2, \"action\": \"deny\", \"condition\": \"ip in blacklist\" }
  ]
}";

    public Task ApplyFixAsync(string incidentId) => Task.CompletedTask;

    public Task<string> LoadPolicyAsync() => Task.FromResult(_policy);

    public Task SavePolicyAsync(string policyJson)
    {
        _policy = policyJson;
        return Task.CompletedTask;
    }
}
