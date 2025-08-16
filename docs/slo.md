# Service Level Objectives – AegisAPI



## Key Performance Indicators

- **p95 latency** ≤ 150ms  

- **Block accuracy** ≥ 98%  

- **False positive rate** ≤ 0.5%  

- **MTTR (Mean Time to Remediation)** < 15 minutes  



## SLOs & Thresholds

---

| **Objective**              | **Target**        | **Alert Thresholds**                          |
|-----------------------------|------------------|-----------------------------------------------|
| p95 request latency         | ≤ 150ms          | Warn ≥ 120ms for 5m, Critical ≥ 150ms for 5m  |
| Block accuracy              | ≥ 98%            | Warn ≤ 98.5% daily, Critical ≤ 98% daily      |
| False positives             | ≤ 0.5%           | Warn ≥ 0.4% daily, Critical ≥ 0.5% daily      |
| MTTR                        | < 15 minutes     | Warn ≥ 12m rolling avg, Critical ≥ 15m        |

---


## Measurement

- **Latency** – captured via OpenTelemetry traces (exported to Prometheus/Grafana).  

- **Accuracy & FP rates** – validated through security test suites (ZAP, k6) + canary analysis.  

- **MTTR** – measured from alert trigger → merged remediation PR.  



## Error Budget

- Latency: 5% of requests may exceed SLO.  

- Accuracy: up to 2% missed attacks within evaluation period.  

- False positives: max 0.5% requests wrongly blocked per day.  



## Alerting Channels

- PagerDuty for critical events.  

- GitHub Issues auto-filed for repeated SLO breaches.  

- Weekly SLO reports posted to Slack/Teams.  

