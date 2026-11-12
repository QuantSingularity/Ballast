# Model service

`code/ai_models` is a Python package (`ballast_ai`) plus a FastAPI service that exposes it to the .NET API. The service is
internal: it has no authentication and binds to `127.0.0.1` by default. Never expose it to the internet.

```bash
cd code/ai_models
python -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
.venv/bin/python -m ballast_ai.service --host 127.0.0.1 --port 8000     # or scripts/start_dev.sh
.venv/bin/python -m pytest
```

JSON uses camelCase, dates are `yyyy-MM-dd`. Errors have the platform shape `{ "code": "...", "message": "..." }`.

| Status | Code               | Meaning                                                            |
| ------ | ------------------ | ------------------------------------------------------------------ |
| 400    | `validation_error` | Bad input (message names the field)                                |
| 409    | `model_not_fitted` | The anomaly detector has not been fitted since the service started |
| 500    | `internal_error`   | Unexpected error (logged, details not returned)                    |

## Endpoints

| Method and path                 | Purpose                                                                                                                 |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `GET /health`                   | `{ status, version }`                                                                                                   |
| `GET /v1/models`                | Model catalog, anomaly detector status, hedging agent episodes and greedy policy table (trains the agent on first call) |
| `POST /v1/forecast`             | Ensemble cash forecast with calibrated intervals                                                                        |
| `POST /v1/forecast/backtest`    | Rolling-origin backtest against a seasonal-naive baseline                                                               |
| `POST /v1/anomaly/fit`          | Fits the payment anomaly detector on outflow history (held in memory)                                                   |
| `POST /v1/anomaly/assess-batch` | Scores payments; `409` if not fitted                                                                                    |
| `POST /v1/regimes/fit`          | Fits a two-state HMM, returns parameters (stateless)                                                                    |
| `POST /v1/regimes/filter`       | Filtered regime probabilities for given parameters and returns (stateless)                                              |
| `POST /v1/risk/fx-var`          | FX value at risk from regime-switching simulation                                                                       |
| `POST /v1/hedging/recommend`    | Hedge ratio the trained agent suggests for each query                                                                   |

Identical forecast and backtest requests are answered from a 64-entry in-process cache.

### Forecast

```json
POST /v1/forecast
{ "series": [ ...daily net cash flow, 60 to 5000 values... ], "startDate": "2025-05-14",
  "horizon": 91, "confidence": 0.9, "origins": 20 }
```

`horizon` 7-180, `confidence` 0.5-0.99, `origins` (calibration refits) 0-40. The response has `dates`, `point`, `lower`,
`upper`, `cumPoint`, `cumLower`, `cumUpper`, `weightHoltWinters`, `weightRidge`, `calibrationOrigins`, `confidence` and
`cumErrorSamples` (one row of cumulative out-of-sample errors per calibration origin, used for cash at risk).

### Anomaly screening

```json
POST /v1/anomaly/fit           { "records": [ { "accountId": "ACC-1", "counterparty": "Globex Materials", "amount": -21000, "date": "2026-03-10" } ] }
POST /v1/anomaly/assess-batch  { "items": [ { "accountId": "ACC-1", "counterparty": "Brand New LLC", "amount": 900000, "valueDate": "2026-03-10" } ] }
```

Amounts should be in one common currency; the API converts to the base currency first. Each result has `score`,
`threshold`, `robustZ`, `newCounterparty`, `flagged` and `reasons`.

### Regimes, FX VaR and hedging

```json
POST /v1/regimes/fit      { "returns": [ ...60 to 20000 daily log returns... ] }
POST /v1/regimes/filter   { "params": { "means": [...], "sigmas": [...], "transition": [[...],[...]], "initial": [...] }, "returns": [ ... ] }
POST /v1/risk/fx-var      { "params": { ... }, "recentReturns": [ ... ], "exposureBase": 1000000, "horizonDays": 21, "confidence": 0.95, "seed": 31 }
POST /v1/hedging/recommend { "queries": [ { "bucketIndex": 0, "currentRatio": 0.0, "highVolProbability": 0.9 } ] }
```

`/v1/hedging/recommend` returns `{ ratios, episodes }`. The ratio is one of 0, 0.25, 0.5, 0.75, 1.0; the API clamps it into
the treasury policy band.

## Package layout

| Folder                                         | Contents                                                                                                              |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `ballast_ai/forecasting`                       | Damped Holt-Winters (vectorized grid search), ridge regression, calendar features, ensemble with calibrated intervals |
| `ballast_ai/anomaly`                           | Isolation forest (array-backed trees, vectorized scoring), payment anomaly detector                                   |
| `ballast_ai/generative`                        | Gaussian hidden Markov model (Baum-Welch), regime-switching simulator                                                 |
| `ballast_ai/environments`, `ballast_ai/agents` | FX hedging environment and tabular Q-learning agent                                                                   |
| `ballast_ai/research`                          | Backtest, risk metrics and FX VaR, synthetic cash flow generator, model catalog                                       |
| `ballast_ai/service`                           | FastAPI application, request and response schemas, process state                                                      |
| `examples`, `tests`                            | Runnable examples and the pytest suite                                                                                |

## Operating notes

- Run one replica: the fitted anomaly detector lives in process memory. The API refits automatically after a restart.
- A cold service takes about half a second to train the hedging agent on first use; a full 91-day forecast with 20
  calibration origins takes about a quarter of a second (40 origins, as used for the risk metrics, about 0.4 s).
- Set `BALLAST_AI_SERVICE_URL` on the API to point at it (default `http://localhost:8000`).
