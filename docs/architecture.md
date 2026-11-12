# Architecture

## Components

```
 Angular web app ----\
                       >-- HTTPS/JSON --> ASP.NET Core API (code/backend/app) ---- HTTP/JSON ---> Python model service
 Blazor mobile app --/                         |                                                   (code/ai_models)
                                               +-- services: forecast, cash, fx, risk, debt, payments, netting, auth
                                               +-- treasury / risk libraries (pure functions)
                                               +-- ai_client (typed HTTP client for the model service)
                                               +-- infrastructure (password hashing, JWT, audit chain)
                                               +-- db (repositories, JSON snapshot) and market_data (rates, FX math)
```

The models are written in Python (numpy) and run as a small FastAPI service. The API is the only caller. See
[MODEL_SERVICE.md](MODEL_SERVICE.md) for the service contract.

## Projects and dependency direction

| Project                  | Depends on       | Responsibility                                                                                            |
| ------------------------ | ---------------- | --------------------------------------------------------------------------------------------------------- |
| `ballast_ai` (Python)    | numpy, FastAPI   | Forecasting, anomaly detection, HMM, RL agent and environment, risk metrics, synthetic data, HTTP service |
| `Ballast.Core`           | nothing          | Domain records, settings, exception types, seeded random numbers                                          |
| `Ballast.MarketData`     | Core             | Rate provider interface, synthetic provider, FX math (conversion, forwards, mark to market)               |
| `Ballast.Data`           | Core             | Repositories, JSON snapshot persistence, deterministic demo data seeder                                   |
| `Ballast.Treasury`       | Core, MarketData | Cash positions, netting, exposure, hedge advisor, effectiveness, covenants, payment workflow              |
| `Ballast.Risk`           | Core, Treasury   | Cash at risk, stress tests, limits                                                                        |
| `Ballast.AiClient`       | Core             | Typed client for the model service, result records, error mapping                                         |
| `Ballast.Infrastructure` | Core, Data       | PBKDF2 hashing, HS256 JWT, hash-chained audit log                                                         |
| `Ballast.Api`            | all of the above | Endpoints, services that orchestrate the libraries and the model service, auth handler, middleware        |

`Treasury` and `Risk` are deliberately pure: they take data in and return records, with no I/O and no shared state. The
hedge advisor receives the agent as a function, which is how it stays testable without the model service.

## Key flows

**Forecast.** `ForecastService` aggregates the seeded transactions into a daily net series per scope (group in the base
currency, a currency, or one account) and posts it to `POST /v1/forecast`. The service runs the ensemble (a 28-day holdout
sets the blend weights), then a rolling-origin calibration that produces interval widths and cumulative error samples. The
service caches identical requests and the API caches results per scope, horizon and confidence.

**Exposure and hedging.** Each foreign currency is forecast in its native currency. `ExposureAggregator` buckets the
forecast net flow into 0-30, 31-60 and 61-91 days and subtracts active, directionally matching hedges. `HedgeAdvisor` sends one
batched request to `POST /v1/hedging/recommend` (bucket, current ratio, regime probability from the currency's hidden Markov
model) and clamps each answer into a policy band for that bucket.

**Payments.** `PaymentService` screens a new payment through `POST /v1/anomaly/assess-batch` (isolation forest plus
per-beneficiary robust z-score) and checks for duplicates. `PaymentWorkflow` then enforces roles, maker-checker, distinct
approvers, escalation to two approvals above the threshold or when flagged, and a release step that must be performed by
someone who did not approve. Every state change is written to the audit chain.

**Model service lifecycle.** The service holds two pieces of state: the trained hedging agent (deterministic, trained from
a fixed seed on first use, about half a second) and the fitted anomaly detector. The API fits the detector once from the
seeded outflow history. If the service restarts and answers `409 model_not_fitted`, `ModelHub` refits it and retries the
call once. Regime models are stateless: the API keeps the fitted parameters and sends them back with each request.

**Failure mode.** If the service is unreachable, endpoints that need a model return `503 ai_unavailable`; everything else
(cash, debt, netting, payments without screening data, audit) keeps working. `GET /health` reports `aiService: "up"|"down"`
without failing the liveness check.

## Design decisions and their costs

| Decision                                                                                                           | Why                                                                                                                                                              | Cost                                                                                                                                                                                                                                      |
| ------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Models in Python behind an HTTP service                                                                            | The numerical and ML ecosystem is Python, it matches the rest of the organization, and the models can be developed, tested and deployed independently of the API | A second runtime, a network hop, and an extra process to run. The service has no authentication and must stay on a private network. Calls are synchronous (the service layer is synchronous), so a slow model call holds a request thread |
| Stateful anomaly detector                                                                                          | Fitting on thousands of records per request would be wasteful                                                                                                    | State lives in one process, so the service runs one replica. A restart is handled by the refit-and-retry path above                                                                                                                       |
| Two synthetic data generators                                                                                      | The backend seeds demo data in C#; the Python package has its own generator for tests and examples                                                               | The two do not produce identical series. The forecaster is given real series by the API, so this only affects test data                                                                                                                   |
| Backend still has no NuGet dependencies                                                                            | Builds with no package feed                                                                                                                                      | Hand-written JWT, hashing and numeric helpers instead of vetted packages. A package such as `Microsoft.AspNetCore.Authentication.JwtBearer` is the more conventional choice                                                               |
| In-memory store with JSON snapshot                                                                                 | Simple, deterministic, testable                                                                                                                                  | Single instance only. No transactions. `BallastDb` is the seam to replace with EF Core and PostgreSQL                                                                                                                                     |
| Synthetic market data                                                                                              | No external dependency, reproducible tests                                                                                                                       | Metrics describe the generator, not markets. `IRateProvider` is the seam for a real feed                                                                                                                                                  |
| Group series converted at the current spot                                                                         | Keeps one consistent base-currency series                                                                                                                        | Ignores historical FX moves in the history                                                                                                                                                                                                |
| EBITDA is a proxy (12 months of receipts less supplier, payroll, rent and fee outflows)                            | There is no P&L in the system                                                                                                                                    | Covenant ratios are indicative, not compliance-grade                                                                                                                                                                                      |
| Hedge "effectiveness" is a dollar-offset test of hedge notional against forecast exposure under the same rate move | Gives a meaningful over/under-hedge signal                                                                                                                       | It measures coverage, not a full IFRS 9 or ASC 815 effectiveness assessment                                                                                                                                                               |
| RL reward uses the expected variance penalty, not the realized one                                                 | Realized returns have a standard deviation about 5x the signal, and Q-learning could not separate regimes with them                                              | The agent is trained on expected utility; it is still a tabular policy over a toy environment                                                                                                                                             |
| FX VaR is summed across currencies                                                                                 | Conservative and simple                                                                                                                                          | No diversification credit, so it overstates portfolio VaR                                                                                                                                                                                 |

## Persistence

`BallastDb` keeps repositories in `ConcurrentDictionary` instances. If `BALLAST_DATA_PATH` is set, users, payments, hedges,
intercompany balances, netting cycles and the audit log are written to `ballast-state.json` (temp file then atomic move) after
each mutation and reloaded on start. Static data (entities, accounts, transactions, facilities) is regenerated
deterministically on every start, so account balances do not change when payments are released: balances are meant to come
from bank statements, which this system does not ingest. To stop several approved payments from together overdrawing an
account, the release check compares the amount with the balance net of payments already released through the system
(each carries a `releasedAt` timestamp; seeded history without one is treated as already in the balance). The model service persists nothing.

## Testing strategy

- The Python package has its own pytest suite (models, numerics, and the service through FastAPI's test client).
- The .NET API is tested through a real Kestrel host on an ephemeral port **against a real Python model service** that the
  test runner starts, including role enforcement, persistence across restart and rate limiting. A scripted HTTP handler
  covers the restart-and-refit path and the service-down behavior.
- The mobile client is tested against that same real API, and Razor pages are rendered to HTML with `HtmlRenderer`.
- The web app is tested with Vitest and jsdom. Its TypeScript types were verified against live JSON.
- The .NET test projects use a small runner in `code/testkit` rather than xUnit because the development environment had no
  NuGet feed. The attribute and assertion names mirror xUnit, so porting is mostly a package swap.
- **Parity with the original C# models.** The forecasting port was checked against the C# implementation it replaced on
  the same seeded data: the group-level backtest, the cash-at-risk figures (as computed at the time, from 20 calibration origins) and the hedge recommendation amounts matched
  to the last digit. Components that consume random numbers (anomaly scores, the RL policy) use a different random stream and
  are validated by behavior rather than exact values.
