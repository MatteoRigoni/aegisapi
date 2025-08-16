# AegisAPI — Architecture Overview

> An ASP.NET Core (.NET 8) + YARP security gateway with OPA authorization, Azure AD (OIDC), API key support, AI anomaly summarization (Azure OpenAI), and end-to-end observability via OpenTelemetry. Targets AKS for data plane, GitHub Actions for CI, Cosign/Trivy/CodeQL for supply chain, and Helm for delivery.

## 1) Request Flow (Data Plane)

```mermaid
flowchart LR
    Client[Client / Partner] -->|HTTPS (TLS1.2+)| E[Edge: Azure Front Door / AppGW WAF]
    E --> G[Gateway (YARP, .NET 8)]
    subgraph YARP Pipeline (per-request)
      A1[AuthN: OIDC (Azure AD) / API Key] --> A2[AuthZ: OPA (Rego) via local sidecar]
      A2 --> A3[Rate Limit (token bucket, Redis)]
      A3 --> A4[Schema Validation (OpenAPI/JSON Schema)]
      A4 --> A5[WAF Checks (OWASP CRS via Coraza or Edge WAF)]
    end
    G -->|mTLS / JWT| Svc[Backend Service(s)]
```

### Notes

- **Authentication**: Azure AD OIDC (JWT bearer) for users/services; API keys for partners/non-OIDC clients.  
- **Authorization**: YARP middleware queries local OPA sidecar.  
- **Rate Limiting**: Redis-backed token bucket.  

## 2) Control Plane & OPA Decision Point

```mermaid
flowchart TB
    subgraph GitOps & CI/CD
      Repo[Policies Repo (Rego)] --> CI[GitHub Actions: lint, test, checkov]
      CI --> Bundle[OPA Bundle (OCI Artifact)]
      CI --> SBOM[SBOM + Sign (Syft/Cosign)]
    end

    Bundle -->|pull| OPASidecar[OPA Sidecar @ Gateway]
    OPASidecar <-->|/v1/data/aegis/allow| YARPMW[YARP AuthZ Middleware]

    subgraph Runtime Mgmt
      ADM[Admin Portal: policy PRs, approvals]
      Tele[Telemetry: OTel, traces/logs/metrics]
    end

    Tele --> ADM
    ADM --> Repo
```

### Notes

- Policies are versioned, tested, and published as bundles (OCI). Gateways pull bundles on start and periodically. 
- Decision point stays in the gateway (low latency, fail-closed with cached bundles). 

## 3) AI Summarizer Sidecar (Azure OpenAI)

```mermaid
flowchart LR
    OTel[OTel Collector] --> EH[(Event Hub / Kafka)]
    EH --> ADSvc[Anomaly Detector (rules + ML)]
    ADSvc --> Sidecar[AI Summarizer Sidecar (.NET + Azure OpenAI)]
    Sidecar --> GH[GitHub PR bot]
    Sidecar --> Ops[Teams/Email Pager Alerts]
```

### Notes

- Summarizes incident context (recent traces, top error spans, request exemplars, policy denials).
- Can draft remediation PRs (e.g., tighten Rego policy, add rate-limit, update schema). Requires human approval.

## 4) Telemetry (OpenTelemetry)

```mermaid
flowchart LR
    App[Gateway & Services (OTel SDK)] --> Coll[OTel Collector (DaemonSet)]
    Coll -->|OTLP| AM[Azure Monitor / Application Insights]
    Coll -->|OTLP| Logs[Log Analytics]
    Coll -->|OTLP| Jaeger[Jaeger/Tempo (optional)]
    Coll -->|Prom| Prom[Prometheus / Managed Prom]
```

### Notes

- Unified tracing/metrics/logs with baggage/attributes scrubbed of PII.
- Tail-based sampling for high-QOS transactions.

### Security Controls (high level)

- **Zero Trust**: mutual TLS to backends; audience/issuer checks on JWTs; least privilege Azure AD app registrations.
- **Defense in Depth**: Edge WAF + schema validation + OPA + rate limits.
- **Secrets**: Azure Key Vault; CSI Secret Store in AKS; short-lived credentials.
- **Supply Chain**: Cosign-signed images & bundles, SBOM attestation; Trivy/Checkov/CodeQL in CI.
- **Isolation**: Namespaces & NetworkPolicies; minimal pod capabilities; read-only filesystem.

### Assumptions

- Azure subscription with AAD, AKS, Key Vault, Managed Grafana/Monitor available.
- Azure OpenAI access approved (deployment + responsible AI guardrails).
- Backends expose OpenAPI specs (>= 3.0) for validation.

### Trade-offs

- YARP (managed in-proc) vs Envoy (feature-rich xDS): simpler ops vs fewer native filters; OPA sidecar compensates.
- AKS vs ACA: AKS gives maximum control; higher ops overhead vs ACA.
- Dual WAF (edge + local): increases protection and complexity.