namespace Dashboard.Services;

public interface IControlPlaneService
{
    Task ApplyFixAsync(string incidentId);
    Task<string> LoadPolicyAsync();
    Task SavePolicyAsync(string policyJson);
    Task<string> SuggestPolicyPatchAsync(string policyJson);
}
