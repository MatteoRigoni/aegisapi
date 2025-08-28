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
| `aegis.anomalies` | Counter | Anomalies flagged by the detectors |

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

### Local setup

To view the dashboard locally with a Prometheus exporter:

1. **Start an OTLP collector** (prints data to console by default):
   ```bash
   docker run --rm -p 4317:4317 otel/opentelemetry-collector
   ```
2. **Run the gateway** pointing to the collector:
   ```bash
   export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
   dotnet run --project src/gateway
   ```
3. **Start Prometheus** using the sample config at `docs/prometheus.yml`:
   ```bash
   docker run --rm -p 9090:9090 \
     -v $(pwd)/docs/prometheus.yml:/etc/prometheus/prometheus.yml \
     prom/prometheus
   ```
   Adjust the target in `prometheus.yml` if the gateway runs elsewhere.
4. **Start Grafana**:
   ```bash
   docker run --rm -p 3000:3000 grafana/grafana-oss
   ```
5. Open Grafana at `http://localhost:3000`, add a Prometheus data source
   pointing to `http://localhost:9090` and import
   `docs/observability-dashboard.json`.

With these services running, the dashboard will display request counts,
latency, error rates and gateway block counters in real time.
