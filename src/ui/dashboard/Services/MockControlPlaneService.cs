namespace Dashboard.Services;

public class MockControlPlaneService : IControlPlaneService
{
    public Task<string> ApplyFixAsync(string incidentId)
        => Task.FromResult($"PR created for incident {incidentId}: #{Random.Shared.Next(100,999)}");
}
