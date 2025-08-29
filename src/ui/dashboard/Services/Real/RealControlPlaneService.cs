namespace Dashboard.Services.Real;

public class RealControlPlaneService : IControlPlaneService
{
    private readonly HttpClient _http;
    public RealControlPlaneService(HttpClient http) => _http = http;

    public Task ApplyFixAsync(string incidentId)
        => throw new NotImplementedException("Coming soon");

    public Task<string> LoadPolicyAsync()
        => throw new NotImplementedException("Coming soon");

    public Task SavePolicyAsync(string policyJson)
        => throw new NotImplementedException("Coming soon");

    public Task<string> SuggestPolicyPatchAsync(string policyJson)
        => throw new NotImplementedException("Coming soon");
}
