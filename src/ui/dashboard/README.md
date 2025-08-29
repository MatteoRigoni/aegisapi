# Dashboard

Blazor Server security dashboard with mock data.

## Run locally

```bash
cd src/ui/dashboard
dotnet run
```

The app uses mocked services by default. Configure `UseMocks` in `appsettings.json` to switch to real APIs.
Set `GatewayBaseUrl` to the running Gateway instance for control plane calls.
