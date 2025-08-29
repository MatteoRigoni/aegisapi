namespace Dashboard.Services;

public interface IControlPlaneService
{
    Task<string> ApplyFixAsync(string incidentId);
}
