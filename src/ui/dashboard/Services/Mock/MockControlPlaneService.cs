namespace Dashboard.Services.Mock;

public class MockControlPlaneService : IControlPlaneService
{
    private string _policy = """
{
  "routes": [
    {
      "id": "api",
      "path": "/api/{**catchall}",
      "destination": "https://backend/api",
      "authorizationPolicy": "ApiReadOrKey"
    }
  ],
  "rateLimits": [
    { "plan": "free", "rpm": 60 }
  ],
  "waf": [
    { "rule": "SqlInjection", "enabled": true }
  ]
}
""";

    public Task ApplyFixAsync(string incidentId) => Task.CompletedTask;

    public Task<string> LoadPolicyAsync() => Task.FromResult(_policy);

    public Task SavePolicyAsync(string policyJson)
    {
        _policy = policyJson;
        return Task.CompletedTask;
    }

    public Task<string> SuggestPolicyPatchAsync(string policyJson)
    {
        return Task.FromResult(policyJson);
    }
}
