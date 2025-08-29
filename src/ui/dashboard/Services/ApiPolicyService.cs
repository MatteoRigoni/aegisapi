using System.Net.Http;
using System.Text;

namespace Dashboard.Services;

public class ApiPolicyService : IPolicyService
{
    private readonly HttpClient _http;

    public ApiPolicyService(HttpClient http) => _http = http;

    public Task<string> GetPolicyAsync() => _http.GetStringAsync("/cp/waf");

    public Task SavePolicyAsync(string json)
        => _http.PostAsync("/cp/waf", new StringContent(json, Encoding.UTF8, "application/json"));
}
