# Observability

The gateway uses [OpenTelemetry](https://opentelemetry.io/) to emit traces, metrics and logs.
The service is configured with standard ASP.NET Core and HTTP client
instrumentation.  Metrics are exported through OTLP and exposed for
Prometheus scraping at `/metrics`.

## Metrics

Custom counters are provided for gateway specific events:

| Metric | Type | Description |
| ------ | ---- | ----------- |
| `gateway.rate_limit_hits` | Counter | Requests rejected by the rate limiter |
| `gateway.waf_blocks` | Counter | Requests blocked by the WAF |
| `gateway.schema_validation_errors` | Counter | Schema validation failures |

In addition, the built-in ASP.NET Core instrumentation exposes request
count and latency histograms as well as status code dimensions allowing
4xx/5xx monitoring.

## Traces and Logs

Traces and logs are exported using OTLP and include context from ASP.NET
Core and outgoing HTTP calls.  Set the `OTEL_EXPORTER_OTLP_ENDPOINT`
environment variable to direct telemetry to a collector.

## Dashboard

A sample Grafana dashboard is available in
`observability-dashboard.json` which visualizes request rate, latency,
error responses and gateway block counters.
