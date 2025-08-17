# AegisAPI – Secure API Gateway with AI Remediation



**AegisAPI** is a zero-trust API security gateway built on **.NET 8 + YARP**, with **AI anomaly detection** and **auto-remediation PRs** to keep your APIs safe, fast, and compliant.

[![CI – Build & Test](https://github.com/<OWNER>/<REPO>/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/<OWNER>/<REPO>/actions/workflows/ci.yml)

## Quick Start

```bash
# Run the gateway
cd src/gateway
dotnet run

# Test endpoints
curl http://localhost:5000/              # Returns "AegisAPI Gateway up"
curl http://localhost:5000/healthz       # Returns 200 OK
curl http://localhost:5000/api/ping      # Proxies to backend /ping endpoint
```

### ✨ Features

- 🔐 **Authentication & Authorization** via Azure AD (OIDC) + OPA/Rego policies  

- 📊 **Rate Limiting & Quotas** with per-tenant and adaptive rules  

- 📑 **Schema Validation** for REST & gRPC contracts  

- 🛡 **WAF Protections** (SQLi, XSS, SSRF, DoS) with OWASP CRS + custom rules  

- 🤖 **AI Security Summarizer** for anomaly detection + incident reports  

- 🔄 **Auto-Remediation PRs** with GitHub Actions (Cosign, Trivy, CodeQL, Checkov)  



### 📈 Key Performance Indicators

- **p95 latency** ≤ 150ms  

- **Block accuracy** ≥ 98%  

- **False positives** ≤ 0.5%  

- **MTTR (Mean Time to Remediation)** < 15 minutes  



