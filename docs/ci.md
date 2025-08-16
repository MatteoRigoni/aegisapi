\# CI Pipeline



\[!\[CI – Build \& Test](https://github.com/<OWNER>/<REPO>/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/<OWNER>/<REPO>/actions/workflows/ci.yml)

\[!\[CodeQL](https://github.com/<OWNER>/<REPO>/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/<OWNER>/<REPO>/security/code-scanning)

\[!\[SBOM](https://github.com/<OWNER>/<REPO>/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/<OWNER>/<REPO>/actions/workflows/ci.yml)



> Replace `<OWNER>/<REPO>` above with your repository path or leave as-is if this file lives in the same repo.



\## What runs



\- \*\*Build \& Test (.NET 8)\*\*  

&nbsp; Restores with caching, builds Release with warnings as errors, runs tests, and publishes:

&nbsp; - `test-results/\*.trx`

&nbsp; - `coverage.cobertura.xml` (XPlat)



\- \*\*CodeQL (C#)\*\*  

&nbsp; Initializes CodeQL, builds the solution (autobuild), and uploads SARIF to the repository’s \*Code scanning alerts\*.



\- \*\*SBOM (SPDX JSON)\*\*  

&nbsp; Generates `sbom.spdx.json` at the repo root and uploads it as an artifact for audit and supply-chain transparency.



\## Artifacts



\- `test-results` (unit test logs + coverage)

\- `sbom` (`sbom.spdx.json`)



\## Tips



\- Commit `packages.lock.json` and set `USE\_LOCKED\_MODE=true` to enforce deterministic restores.

\- Keep secrets out of CI logs; the pipeline uses least-privilege `permissions` and no custom tokens.



