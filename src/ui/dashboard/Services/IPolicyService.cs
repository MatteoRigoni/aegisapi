namespace Dashboard.Services;

public interface IPolicyService
{
    Task<string> GetPolicyAsync();
    Task SavePolicyAsync(string json);
}
