# Architecture

## Components

```
 Angular web app ----\
                       >-- HTTPS/JSON --> ASP.NET Core API (code/backend/app)
 Blazor mobile app --/                         |
                                               +-- services: forecast, cash, fx, risk, debt, payments, netting, auth
                                               +-- treasury / risk libraries (pure functions)
                                               +-- ai_models library (forecasting, anomaly, HMM, RL)
                                               +-- infrastructure (password hashing, JWT, audit chain)
                                               +-- db (repositories, JSON snapshot) and market_data (rates, FX math)
```

## Projects and dependency direction

| Project                  | Depends on                 | Responsibility                                                                               |
| ------------------------ | -------------------------- | -------------------------------------------------------------------------------------------- |
| `Ballast.AiModels`       | nothing                    | Numerics, forecasting, anomaly detection, HMM, RL agent and environment, synthetic data      |
| `Ballast.Core`           | nothing                    | Domain records, settings, exception types                                                    |
| `Ballast.MarketData`     | Core, AiModels             | Rate provider interface, synthetic provider, FX math (conversion, forwards, mark to market)  |
| `Ballast.Data`           | Core, AiModels             | Repositories, JSON snapshot persistence, deterministic demo seeder                           |
| `Ballast.Treasury`       | Core, MarketData, AiModels | Cash positions, netting, exposure, hedge advisor, effectiveness, covenants, payment workflow |
| `Ballast.Risk`           | Core, Treasury, AiModels   | Cash at risk, stress tests, limits, FX VaR                                                   |
| `Ballast.Infrastructure` | Core, Data                 | PBKDF2 hashing, HS256 JWT, hash-chained audit log                                            |
| `Ballast.Api`            | all of the above           | Endpoints, services that orchestrate the libraries, auth handler, middleware                 |

`Treasury` and `Risk` are deliberately pure: they take data in and return records, with no I/O and no shared state. That is
why most business rules are covered by fast unit tests and why the API services stay thin.

## Key flows

**Forecast.** `ForecastService` aggregates the seeded transactions into a daily net series per scope (group in the base
currency, a currency, or one account), runs the ensemble (28-day holdout sets the blend weights), then runs a rolling-origin
calibration to produce interval widths and the cumulative error samples. Results are cached per scope, horizon and
confidence.

**Exposure and hedging.** Each foreign currency is forecast in its native currency. `ExposureAggregator` buckets the
forecast net flow into 0-30, 31-60 and 61-91 days and subtracts active, directionally matching hedges. `HedgeAdvisor` asks
the trained Q-learning policy what hedge ratio to use given the exposure bucket, the current ratio and the regime reported
by the currency's hidden Markov model, then clamps the answer into a policy band for that bucket.

**Payments.** `PaymentService` screens a new payment (isolation forest plus per-beneficiary robust z-score, duplicate
check), then `PaymentWorkflow` enforces roles, maker-checker, distinct approvers, escalation to two approvals above the
threshold or when flagged, and a release step that must be performed by someone who did not approve. Every state change is
written to the audit chain.

## Design decisions and their costs

| Decision                                                                                                           | Why                                                                                                                 | Cost                                                                                                                                                                                                                      |
| ------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Backend and models use only the .NET shared framework                                                              | Builds with no package feed, tiny supply-chain surface                                                              | Hand-written JWT, hashing, and numerics instead of vetted packages. The JWT and password code are small and tested, but a package such as `Microsoft.AspNetCore.Authentication.JwtBearer` is the more conventional choice |
| In-memory store with JSON snapshot                                                                                 | Simple, deterministic, testable                                                                                     | Single instance only. No transactions. `BallastDb` is the seam to replace with EF Core and PostgreSQL                                                                                                                     |
| Synthetic market data                                                                                              | No external dependency, reproducible tests                                                                          | Metrics describe the generator, not markets. `IRateProvider` is the seam for a real feed                                                                                                                                  |
| Group series converted at the current spot                                                                         | Keeps one consistent base-currency series                                                                           | Ignores historical FX moves in the history                                                                                                                                                                                |
| EBITDA is a proxy (12 months of receipts less supplier, payroll, rent and fee outflows)                            | There is no P&L in the system                                                                                       | Covenant ratios are indicative, not compliance-grade                                                                                                                                                                      |
| Hedge "effectiveness" is a dollar-offset test of hedge notional against forecast exposure under the same rate move | Gives a meaningful over/under-hedge signal                                                                          | It measures coverage, not a full IFRS 9 or ASC 815 effectiveness assessment                                                                                                                                               |
| RL reward uses the expected variance penalty, not the realized one                                                 | Realized returns have a standard deviation about 5x the signal, and Q-learning could not separate regimes with them | The agent is trained on expected utility; it is still a tabular policy over a toy environment                                                                                                                             |
| FX VaR is summed across currencies                                                                                 | Conservative and simple                                                                                             | No diversification credit, so it overstates portfolio VaR                                                                                                                                                                 |

## Persistence

`BallastDb` keeps repositories in `ConcurrentDictionary` instances. If `BALLAST_DATA_PATH` is set, users, payments, hedges,
intercompany balances, netting cycles and the audit log are written to `ballast-state.json` (temp file then atomic move) after
each mutation and reloaded on start. Static data (entities, accounts, transactions, facilities) is regenerated
deterministically on every start, so account balances do not change when payments are released: balances are meant to come
from bank statements, which this system does not ingest.

## Testing strategy

- Libraries are unit tested with fixed seeds.
- The API is tested through a real Kestrel host on an ephemeral port, including role enforcement, persistence across restart
  and rate limiting.
- The mobile client is tested against that same real API, and Razor pages are rendered to HTML with `HtmlRenderer`.
- The web app is tested with Vitest and jsdom. Its TypeScript types were verified against live JSON.
- Tests use a small runner in `code/testkit` rather than xUnit because the development environment had no NuGet feed. The
  attribute and assertion names mirror xUnit, so porting is mostly a package swap.
