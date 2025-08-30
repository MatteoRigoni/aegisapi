# Dashboard

## Running locally

```
dotnet run --project src/ui/dashboard
```

Set `UseMocks` in `appsettings.json` to `true` for seeded data or `false` to call real services. Configure Gateway base URLs under `GatewayUrls`:

```
"GatewayUrls": {
  "Metrics": "https://localhost:5001",
  "Incidents": "https://localhost:5001",
  "Control": "https://localhost:5001"
}
```
