# AegisAPI – Secure API Gateway with AI Remediation



**AegisAPI** is a zero-trust API security gateway built on **.NET 8 + YARP**, with **AI anomaly detection** and **auto-remediation PRs** to keep your APIs safe, fast, and compliant.

[![CI – Build & Test](https://github.com/MatteoRigoni/aegisapi/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/MatteoRigoni/aegisapi/actions/workflows/ci.yml)
[![CodeQL](https://github.com/MatteoRigoni/aegisapi/actions/workflows/codeql.yml/badge.svg?branch=master)](https://github.com/MatteoRigoni/aegisapi/actions/workflows/codeql.yml)
[![SBOM](https://github.com/MatteoRigoni/aegisapi/actions/workflows/sbom.yml/badge.svg?branch=master)](https://github.com/MatteoRigoni/aegisapi/actions/workflows/sbom.yml)

## Quick Start

```bash
# Run the gateway
cd src/gateway
dotnet run

# Test endpoints
curl http://localhost:5000/                             # Returns "AegisAPI Gateway up"
curl http://localhost:5000/healthz                      # Returns 200 OK
curl http://localhost:5000/api/ping                     # Public route
curl -H "Authorization: Bearer <token>" http://localhost:5000/api/secure/ping  # Protected route (JWT)
curl -H "X-API-Key: <key>" http://localhost:5000/api/secure/ping               # Protected route (API key)
```

## Autenticazione

AegisAPI supporta due modalità di autenticazione:

- **JWT**: inviare un token nell'intestazione `Authorization: Bearer <token>`. Per lo sviluppo, impostare il segreto simmetrico tramite `Auth:JwtKey` in `src/gateway/appsettings.Development.json`.
- **API key**: inviare la chiave nell'intestazione `X-API-Key: <chiave>`. Per lo sviluppo, configurare `Auth:ApiKeyHash` nello stesso file con l'hash SHA-256 della chiave (`echo -n tua-chiave | sha256sum`).

### ✨ Features

- 🔐 **Authentication & Authorization** via Azure AD (OIDC) + OPA/Rego policies  

- 📊 **Rate Limiting & Quotas** with per-tenant and adaptive rules  

- 📑 **Schema Validation** for REST & gRPC contracts  

- 🛡 **WAF Protections** (SQLi, XSS, SSRF, DoS) with OWASP CRS + custom rules

- 📉 **Anomaly Detection** with per-client and per-route rolling thresholds, normalized routes, HTTP method & UA entropy features, and optional ML.NET models (see [docs/anomaly-detection](docs/anomaly-detection.md))

- 🤖 **AI Security Summarizer** for anomaly detection + incident reports

- 🔄 **Auto-Remediation PRs** with GitHub Actions (Cosign, Trivy, CodeQL, Checkov)  



### 📈 Key Performance Indicators

- **p95 latency** ≤ 150ms  

- **Block accuracy** ≥ 98%  

- **False positives** ≤ 0.5%  

- **MTTR (Mean Time to Remediation)** < 15 minutes  



