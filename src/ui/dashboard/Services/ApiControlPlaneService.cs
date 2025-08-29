namespace Dashboard.Services;

public class ApiControlPlaneService : IControlPlaneService
{
    public Task<string> ApplyFixAsync(string incidentId)
        => Task.FromResult("Apply fix API available soon.");
}
