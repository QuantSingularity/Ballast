# Ballast

Treasury and liquidity management platform for the QuantSingularity organization. It covers cash positioning, forecasting,
FX hedging, risk, debt, in-house banking and payment approvals, built as a full-stack application with a C# API, Angular
and Blazor clients, and its own AI model library.

## At a glance

| Item    | Detail                                                                                            |
| ------- | ------------------------------------------------------------------------------------------------- |
| Backend | ASP.NET Core on .NET 10, in-memory state with an optional JSON snapshot                           |
| Web     | Angular 22, standalone components and signals                                                     |
| Mobile  | Blazor Razor class library with a MAUI host                                                       |
| Models  | Python 3.11+, numpy and FastAPI: forecasting, anomaly detection, regime detection, hedging policy |
| Data    | Fully synthetic and deterministic. No bank connections, no market data feeds                      |
| Tests   | 207 automated tests across four suites, plus an end-to-end smoke test                             |
| Status  | Working and tested, but not production-ready treasury software.                                   |
| License | MIT                                                                                               |

## Contents

| Section                                 | What you will find                         |
| --------------------------------------- | ------------------------------------------ |
| [What it does](#what-it-does)           | The nine product modules                   |
| [Architecture](#architecture)           | Components, ports and how a request flows  |
| [Project Structure](#project-structure) | Where everything lives                     |
| [Quick start](#quick-start)             | Requirements, commands and demo sign-ins   |
| [Configuration](#configuration)         | The settings you are most likely to change |
| [Security](#security)                   | What is implemented                        |
| [Testing and CI](#testing-and-ci)       | Test suites, scripts and the CI pipeline   |
| [Model quality](#model-quality)         | Measured results with their caveats        |
| [Documentation](#documentation)         | Full docs and component READMEs            |

## What it does

| Module         | Capabilities                                                                                                                                                                                                          |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Cash           | Multi-bank, multi-currency positions by entity, bank and currency. Liquidity ladder with forecast bands                                                                                                               |
| Forecast       | 13-week cash forecast: damped Holt-Winters blended with a calendar-feature ridge regression, rolling-origin calibrated intervals, built-in backtest                                                                   |
| FX and hedging | Forecast exposure by horizon, forward pricing (covered interest parity), hedge book with mark to market and effectiveness test, recommendations from a reinforcement-learning policy bounded by treasury policy bands |
| Risk           | Cash at risk and expected shortfall, FX value at risk with regime-aware simulation, five liquidity stress scenarios, policy limits                                                                                    |
| In-house bank  | Intercompany balances, multilateral netting (gross vs net volume), recorded netting cycles                                                                                                                            |
| Debt           | Facilities, utilization, covenant compliance (liquidity, leverage, interest cover) with headroom                                                                                                                      |
| Payments       | Maker-checker workflow, dual approval above a threshold, AI anomaly screening, duplicate detection                                                                                                                    |
| Audit          | Hash-chained, append-only log with a verification endpoint                                                                                                                                                            |
| Mobile         | Approvals, cash position, forecast, alerts and FX exposure on a phone                                                                                                                                                 |

## Architecture

```
  Web (Angular)  ----\
                      >----  ASP.NET Core API  ----  Model service (FastAPI)
  Mobile (Blazor) ---/       |                       forecasting, anomaly,
                             |                       regimes, hedging policy
                  in-memory state + JSON snapshot
                  hash-chained audit log
```

| Component     | Technology                                 | Responsibility                                                                   | Default port |
| ------------- | ------------------------------------------ | -------------------------------------------------------------------------------- | ------------ |
| Web           | Angular 22, standalone components, signals | Dashboard, cash, forecast, FX, risk, debt, in-house bank, payments, audit        | 4200         |
| Mobile        | Blazor Razor class library, MAUI host      | Approvals, cash position, forecast, alerts, FX exposure                          | n/a          |
| API           | ASP.NET Core, .NET 10                      | Authentication, roles, payment workflow, treasury and risk logic, persistence    | 5000         |
| Model service | Python 3.11+, FastAPI, numpy               | Forecast ensemble, anomaly detection, HMM regimes, reinforcement-learning policy | 8000         |

## Project Structure

```text
Ballast/
├── code/
│   ├── backend/             # ASP.NET Core API and .NET domain libraries (C#)
│   │   ├── app/             # API host, endpoints (v1), services, DTOs
│   │   ├── ai_client/       # HTTP client for the model service
│   │   ├── core/            # Domain model, settings, exceptions
│   │   ├── db/              # Repositories, JSON snapshot, demo seeder
│   │   ├── infrastructure/  # Password hashing, JWT, hash-chained audit log
│   │   ├── market_data/     # Rate provider and FX math
│   │   ├── risk/            # Cash at risk, stress tests, limits
│   │   ├── treasury/        # Netting, exposure, hedging, covenants, payment workflow
│   │   └── tests/           # Unit and API integration tests
│   ├── ai_models/           # Python models and FastAPI service
│   │   ├── ballast_ai/      # Forecasting, anomaly, agents, generative, research, service
│   │   ├── examples/        # Runnable usage examples
│   │   └── tests/           # pytest suite
│   └── testkit/             # Test runner shared by the .NET test projects
├── web-frontend/            # Angular 22 dashboard
├── mobile-frontend/         # Blazor mobile app: UI library, tests, MAUI host
├── infrastructure/          # Dockerfiles, compose, Kubernetes manifests
├── scripts/                 # Setup, dev, build, test, lint, smoke test, release
├── docs/                    # Architecture, API, configuration, security, results
└── README.md
```

## Quick start

| Requirement | Version       | Needed for                               |
| ----------- | ------------- | ---------------------------------------- |
| .NET SDK    | 10 or newer   | API, mobile library, .NET tests          |
| Node.js     | 22 or newer   | Web application (skip with `--api-only`) |
| Python      | 3.11 or newer | Model service and its tests              |

| Step | Command                          | Result                                              |
| ---- | -------------------------------- | --------------------------------------------------- |
| 1    | `./scripts/setup_environment.sh` | Checks tools, installs web packages, creates `.env` |
| 2    | `./scripts/start_dev.sh`         | Model service on :8000, API on :5000, web on :4200  |
| 3    | Open http://localhost:4200       | Sign in with a demo user below                      |

| Variation | Command                                                  |
| --------- | -------------------------------------------------------- |
| No Node   | `./scripts/start_dev.sh --api-only`                      |
| Docker    | See [infrastructure/README.md](infrastructure/README.md) |

Demo sign-ins exist in development only. The password for all of them is `Ballast-Demo-2026!`.

| User                                                    | Role      | Can do                                       |
| ------------------------------------------------------- | --------- | -------------------------------------------- |
| `treasurer@ballast.example`                             | Treasurer | Creates payments, books hedges, runs netting |
| `approver@ballast.example`, `approver2@ballast.example` | Approver  | Approves and rejects payments                |
| `admin@ballast.example`                                 | Admin     | Administers users and the platform           |
| `analyst@ballast.example`                               | Read-only | Views everything, changes nothing            |

## Configuration

The API reads environment variables at startup and refuses invalid combinations with an explicit message. The settings
below are the ones most often changed; the full list is in [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

| Variable                          | Default                     | Purpose                                                           |
| --------------------------------- | --------------------------- | ----------------------------------------------------------------- |
| `BALLAST_ENV`                     | `development`               | `development`, `staging` or `production`                          |
| `BALLAST_SECRET_KEY`              | none (required)             | Signs JWTs. At least 32 characters                                |
| `BALLAST_AI_SERVICE_URL`          | `http://localhost:8000`     | Where the API finds the model service                             |
| `BALLAST_DATA_PATH`               | unset                       | Directory for the JSON snapshot. Unset keeps state in memory only |
| `BALLAST_CORS_ORIGINS`            | `http://localhost:4200`     | Comma-separated allowed origins                                   |
| `BALLAST_DUAL_APPROVAL_THRESHOLD` | `50000`                     | Amount above which two approvals are required                     |
| `BALLAST_TRUST_PROXY`             | `false`                     | Honor `X-Forwarded-*` headers, only from trusted networks         |
| `BALLAST_TRUSTED_PROXIES`         | loopback and private ranges | CIDR networks allowed to set forwarded headers                    |
| `BALLAST_AUTH_RATE_LIMIT`         | `30`                        | Auth requests per minute per client IP                            |

## Security

| Area          | Implementation                                                                                                                  |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Passwords     | PBKDF2-SHA256, 210,000 iterations by default, fixed-time comparison, 12 character minimum with mixed case and a digit           |
| Tokens        | HS256 JWTs, 30 minute access and 7 day refresh by default, a `typ` claim so token kinds cannot be swapped, `alg: none` rejected |
| Sign-in       | Same message for unknown accounts, 5 failures per client and account lock for 5 minutes, 25 across all clients, bounded tracker |
| Rate limiting | 30 auth requests per minute per client IP. Forwarded headers are honored only from `BALLAST_TRUSTED_PROXIES`                    |
| Authorization | Role policies plus workflow rules: creators cannot approve, approvers cannot approve twice, releasers cannot be approvers       |
| Payments      | Atomic creation (duplicate check, insert, audit) and release checked against balance net of earlier releases                    |
| Audit         | Hash-chained, append-only log with a verification endpoint                                                                      |
| Startup       | Missing or weak secrets, malformed URLs, weak hashing in staging or production, and default demo passwords all stop the API     |
| Supply chain  | CI actions pinned to commit SHAs, dependency audits for Python, npm and NuGet, Dependabot for every ecosystem                   |

The full list, including the gaps, is in [docs/SECURITY.md](docs/SECURITY.md).

## Testing and CI

| Script                    | What it does                                                                                     |
| ------------------------- | ------------------------------------------------------------------------------------------------ |
| `./scripts/run_tests.sh`  | Runs all four suites and prints a summary                                                        |
| `./scripts/lint_code.sh`  | `dotnet format`, ruff, prettier, shellcheck and a text policy check (`--fix` applies formatting) |
| `./scripts/smoke_test.sh` | Publishes the API, starts it, signs in and calls the main endpoints                              |

| Suite     | Tests | What it covers                                                                                                                                                                               |
| --------- | ----- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AI models | 41    | Forecasting, anomaly detection, HMM, RL policy, risk metrics, the FastAPI service                                                                                                            |
| Backend   | 97    | Unit tests plus integration tests against a real Kestrel host: auth, roles, workflow, persistence, rate limiting, proxy trust, lockout scoping, release netting, concurrent payment creation |
| Mobile UI | 30    | The real API client against a real API, token refresh, approval workflow, every Razor page rendered to HTML                                                                                  |
| Web       | 39    | Auth, interceptor refresh and retry, guards, charts, role-aware page behavior                                                                                                                |
| Total     | 207   |                                                                                                                                                                                              |

The Angular types were also diffed against live API JSON for 35 endpoint shapes with zero mismatches.

| CI job                                | What it checks                                                   | Gate     |
| ------------------------------------- | ---------------------------------------------------------------- | -------- |
| Backend (with the real model service) | The .NET solution tests against a live model service             | Required |
| Mobile UI library and tests           | Build and test the shared Razor library                          | Required |
| Web frontend test and build           | Angular unit tests and production build                          | Required |
| Infrastructure validation             | YAML syntax, plus compose and kustomize checks where tools exist | Required |
| Dependency vulnerability audit        | `pip-audit`, `npm audit`, NuGet advisories including transitive  | Required |
| Documentation links                   | Relative links in the Markdown files resolve                     | Required |
| MAUI host build (Android)             | Compiles the platform host                                       | Advisory |

## Model quality

Measured on the seeded group-level history with a 28-day horizon and 8 rolling origins. Full numbers and caveats are in
[docs/MODEL_RESULTS.md](docs/MODEL_RESULTS.md).

| Measure                     | Result                                 | How to read it                                                                      |
| --------------------------- | -------------------------------------- | ----------------------------------------------------------------------------------- |
| Forecast accuracy (MASE)    | 0.70 against a seasonal-naive baseline | Better than the baseline, but Holt-Winters alone is as accurate on daily error      |
| 90% interval coverage       | 79.5% of outcomes                      | Intervals are somewhat optimistic                                                   |
| Cash at risk sample quality | Low reliability                        | Overlapping windows leave few independent samples, so tail estimates are indicative |

## Documentation

[docs/README.md](docs/README.md) is the full index.

| Document                                   | Contents                                                           |
| ------------------------------------------ | ------------------------------------------------------------------ |
| [Architecture](docs/architecture.md)       | Components, dependency direction, key flows, design decisions      |
| [API](docs/API.md)                         | Every endpoint, roles, error format, example payloads              |
| [Model service](docs/MODEL_SERVICE.md)     | The Python service: endpoints, request shapes, operating notes     |
| [Configuration](docs/CONFIGURATION.md)     | Environment variables and startup safety checks                    |
| [Installation](docs/INSTALLATION.md)       | Requirements, setup, Docker, mobile                                |
| [Usage](docs/USAGE.md)                     | Walkthroughs for the dashboard, hedging, payments, netting, mobile |
| [Feature matrix](docs/FEATURE_MATRIX.md)   | Feature by API, web, mobile and test coverage                      |
| [Model results](docs/MODEL_RESULTS.md)     | Measured model quality with caveats                                |
| [Security](docs/SECURITY.md)               | What is implemented and the known gaps                             |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Symptoms and fixes                                                 |
| [Contributing](docs/CONTRIBUTING.md)       | Workflow, conventions, formatting, adding an endpoint              |

| Component README                           | Covers                                |
| ------------------------------------------ | ------------------------------------- |
| [Backend](code/backend/README.md)          | The .NET solution                     |
| [AI models](code/ai_models/README.md)      | The Python model library and service  |
| [Web](web-frontend/README.md)              | The Angular application               |
| [Mobile](mobile-frontend/README.md)        | The Blazor library and MAUI host      |
| [Infrastructure](infrastructure/README.md) | Docker, compose and Kubernetes        |
| [Scripts](scripts/README.md)               | Build, test, lint and release scripts |

## License

MIT. See [LICENSE](LICENSE).
