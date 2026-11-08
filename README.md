# Ballast

Treasury and liquidity management platform for the QuantSingularity organization: a full-stack application built with
C#, ASP.NET Core, Angular (web) and Blazor (mobile), with its own AI model library.

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

## Repository layout

The layout follows the other full-stack repositories in the organization (`code/`, `web-frontend/`,
`mobile-frontend/`, `infrastructure/`, `scripts/`, `docs/`).

```
Ballast/
  code/
    ai_models/        forecasting, anomaly, agents, environments, generative, research, examples, tests
    backend/
      app/            ASP.NET Core API: endpoints (api/v1), services, schemas
      core/           domain model, settings, exceptions
      db/             repositories, snapshot persistence, demo data seeder
      market_data/    rate provider, FX math
      treasury/       positions, netting, exposure, hedge advisor, covenants, payment workflow
      risk/           cash at risk, stress tests, limits, FX VaR
      infrastructure/ password hashing, JWT, audit log
      tests/          unit and API integration tests
    testkit/          small test runner shared by the .NET test projects
  web-frontend/       Angular 22 application (standalone components, signals)
  mobile-frontend/    Blazor: shared Razor class library, tests, and a MAUI host
  infrastructure/     Dockerfiles, compose, Kubernetes manifests
  scripts/            build, test, lint, dev server, smoke test, release
  docs/               architecture, API, configuration, security, results
```

## Quick start

Requirements: .NET 10 SDK and Node.js 22 or newer.

```bash
./scripts/setup_environment.sh   # checks tools, installs web packages, creates .env
./scripts/start_dev.sh           # API on :5000, web on :4200
```

Open http://localhost:4200 and sign in with a demo user (development only, password `Ballast-Demo-2026!`):

| User                                                | Role                                                     |
| --------------------------------------------------- | -------------------------------------------------------- |
| treasurer@ballast.example                           | Treasurer (creates payments, books hedges, runs netting) |
| approver@ballast.example, approver2@ballast.example | Approver (approves and rejects payments)                 |
| admin@ballast.example                               | Admin                                                    |
| analyst@ballast.example                             | Read-only                                                |

Without Node: `./scripts/start_dev.sh --api-only`. With Docker: see [infrastructure/README.md](infrastructure/README.md).

## Tests

```bash
./scripts/run_tests.sh      # all suites
./scripts/lint_code.sh      # formatting, shellcheck, text policy
./scripts/smoke_test.sh     # publishes the API, starts it, signs in, calls the main endpoints
```

| Suite     | Tests | What it covers                                                                                                    |
| --------- | ----- | ----------------------------------------------------------------------------------------------------------------- |
| AI models | 17    | Forecasting, anomaly detection, HMM, RL policy, risk metrics                                                      |
| Backend   | 71    | Unit tests plus integration tests against a real Kestrel host (auth, roles, workflow, persistence, rate limiting) |
| Mobile UI | 30    | The real API client against a real API, token refresh, approval workflow, every Razor page rendered to HTML       |
| Web       | 38    | Auth, interceptor refresh and retry, guards, charts, role-aware page behavior                                     |

The Angular types were also diffed against live API JSON for 35 endpoint shapes with zero mismatches.

## Measured model quality

On the seeded group-level history (28-day horizon, 8 rolling origins), the forecast ensemble has a MASE of 0.70 against a
seasonal-naive baseline, but its 90% intervals cover only 79.5% of outcomes, so they are somewhat optimistic. Holt-Winters
alone is as accurate as the ensemble on daily error. Full numbers and caveats: [docs/MODEL_RESULTS.md](docs/MODEL_RESULTS.md).

## Read this before relying on it

This is a working, tested application, but it is not production-ready treasury software.

- **Data is synthetic.** Accounts, transactions, rates and volatility are generated deterministically. There are no bank
  connections or market data feeds. The models are trained on that synthetic history, so their metrics describe the
  generator, not real markets.
- **Single instance.** State is held in memory with an optional JSON snapshot. Running several replicas would diverge.
  Replace `BallastDb` with a shared database before scaling out.
- **Mobile host is unverified.** The MAUI project could not be built where this was developed. Everything with logic
  (`Ballast.Mobile.UI`) is built and tested; the host only adds platform boilerplate. See
  [mobile-frontend/Ballast.Mobile.App/README.md](mobile-frontend/Ballast.Mobile.App/README.md).
- **Docker and Kubernetes are unverified.** The YAML is syntax-checked, but images were not built and manifests were not
  applied (no Docker daemon or cluster available).
- **No MFA, no token revocation.** See [docs/SECURITY.md](docs/SECURITY.md) for the full list.

## Documentation

[docs/README.md](docs/README.md) is the index: architecture, API reference, configuration, installation, usage walkthroughs,
feature matrix, model results, security, troubleshooting and contributing.

## License

MIT. See [LICENSE](LICENSE).
