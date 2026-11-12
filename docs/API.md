# API reference

Base URL in development: `http://localhost:5000`. All bodies are JSON with camelCase names, enums are strings, dates are
`yyyy-MM-dd`. List all routes at runtime with `GET /api/v1/meta/endpoints` (authenticated).

## Conventions

**Authentication.** Send `Authorization: Bearer <accessToken>`. Access tokens last 30 minutes by default, refresh tokens 7 days.

**Roles.** `Analyst` (read only), `Treasurer` (maker), `Approver` (checker), `Admin` (both, plus user management).

**Errors.** Every error has the same shape:

```json
{ "code": "validation_error", "message": "Amount must be greater than zero" }
```

| Status | Code               | Meaning                                                                                      |
| ------ | ------------------ | -------------------------------------------------------------------------------------------- |
| 400    | `validation_error` | Bad input                                                                                    |
| 401    | `unauthorized`     | Missing, invalid or expired token, or bad credentials                                        |
| 403    | `forbidden`        | Role or maker-checker rule                                                                   |
| 404    | `not_found`        | Unknown id or scope                                                                          |
| 409    | `conflict`         | Wrong state for the action, duplicate, insufficient available balance                        |
| 423    | `locked`           | 5 failed sign-ins for an account from one client address, or 25 from all clients (5 minutes) |
| 429    | none               | Auth rate limit exceeded                                                                     |
| 502    | `ai_service_error` | The model service rejected a request or returned an unusable response                        |
| 503    | `ai_unavailable`   | The model service is unreachable. Endpoints that do not need a model keep working            |
| 500    | `internal_error`   | Unexpected error (details are logged, not returned)                                          |

## Public

| Method and path | Description                                                                                                                                                                     |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /`         | Service name and version                                                                                                                                                        |
| `GET /health`   | `{ status, environment, asOf, baseCurrency, aiService }`. `status` is always `ok` while the API is alive; `aiService` is `up` or `down` (one second probe of the model service) |

## Authentication (rate limited per client IP)

| Method and path                   | Role  | Description                                                                                                                                                                                                                                |
| --------------------------------- | ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `POST /api/auth/register`         | none  | `{ email, password, name }`. Password: 12+ characters with upper, lower and digit. First user becomes Admin, later users Analyst. Returns 403 once the system has users if self-registration is disabled (the default outside development) |
| `POST /api/auth/login`            | none  | `{ email, password }` returns tokens                                                                                                                                                                                                       |
| `POST /api/auth/refresh`          | none  | `{ refreshToken }` returns a new pair                                                                                                                                                                                                      |
| `GET /api/auth/profile`           | any   | Current user                                                                                                                                                                                                                               |
| `GET /api/auth/users`             | Admin | All users                                                                                                                                                                                                                                  |
| `POST /api/auth/users`            | Admin | `{ email, name, password, role }` creates a user with the given role                                                                                                                                                                       |
| `PATCH /api/auth/users/{id}/role` | Admin | `{ role }`. Admins cannot change their own role                                                                                                                                                                                            |

```json
{
  "user": {
    "id": "U-002",
    "email": "treasurer@ballast.example",
    "name": "Taylor Treasurer",
    "role": "Treasurer"
  },
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresIn": 1800
}
```

## Cash and forecast

| Method and path                                      | Description                                                                              |
| ---------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `GET /api/v1/cash/summary`                           | Total in base currency, by currency, entity and bank                                     |
| `GET /api/v1/cash/accounts`                          | All accounts with native and base balances                                               |
| `GET /api/v1/cash/ladder`                            | Projected balance with bounds at 7, 30 and 91 days, plus scheduled payments              |
| `GET /api/v1/cash/transactions?accountId&days&limit` | Recent transactions (days 1-540, limit 1-500)                                            |
| `GET /api/v1/forecast?scope&horizonDays&confidence`  | `scope`: `all`, `currency:EUR`, `account:ACC-DE-001`. Horizon 7-180, confidence 0.5-0.99 |
| `GET /api/v1/forecast/backtest?scope&horizonDays`    | Rolling-origin backtest (horizon 7-91)                                                   |

Forecast response (abridged):

```json
{
  "scope": "all",
  "currency": "USD",
  "horizonDays": 7,
  "confidence": 0.9,
  "model": {
    "name": "cash-forecast-ensemble",
    "weightHoltWinters": 0.4581,
    "weightRidge": 0.5419,
    "calibrationOrigins": 20
  },
  "daily": [
    {
      "date": "2026-11-05",
      "point": 127664.76,
      "lower": -91621.13,
      "upper": 346950.65,
      "cumulative": 127664.76,
      "cumulativeLower": -889813.42,
      "cumulativeUpper": 545174.94
    }
  ],
  "history": [{ "date": "2026-09-07", "net": 81234.5 }]
}
```

## FX and hedging

| Method and path                      | Role             | Description                                                                                           |
| ------------------------------------ | ---------------- | ----------------------------------------------------------------------------------------------------- |
| `GET /api/v1/fx/rates`               | any              | Spot (USD per unit), interest rate, 30 and 90 day forwards, probability of the high-volatility regime |
| `GET /api/v1/fx/exposures`           | any              | Net forecast exposure by currency and horizon bucket, hedged amount, ratio, open amount               |
| `GET /api/v1/fx/recommendations`     | any              | Policy-bounded hedge recommendations                                                                  |
| `GET /api/v1/fx/regimes`             | any              | HMM parameters per currency                                                                           |
| `GET /api/v1/fx/hedges`              | any              | Hedge book with mark to market and effectiveness                                                      |
| `POST /api/v1/fx/hedges`             | Treasurer, Admin | `{ currency, direction, notional, tenorDays, counterparty }`. Booked at the forward rate              |
| `POST /api/v1/fx/hedges/{id}/cancel` | Treasurer, Admin | Cancels an active hedge                                                                               |

Recommendation example:

```json
{
  "currency": "CHF",
  "bucket": "0-30 days",
  "targetRatio": 0.9,
  "currentRatio": 0,
  "direction": "SellForeign",
  "recommendedNotional": 264117,
  "highVolProbability": 0.578,
  "rationale": "Agent policy in the high-volatility regime suggests 100%; policy band for 0-30 days is 50% to 90%, target 90%"
}
```

## Risk and debt

| Method and path                     | Description                                                                                                                                              |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/v1/risk/metrics`          | Cash at risk (30 and 91 days, 95% and 99%) with `samples`, `effectiveSamples`, `reliability` and `method`, liquidity, coverage ratio, FX VaR by currency |
| `GET /api/v1/risk/stress-scenarios` | FX shock, bank failure, collections delay, rates +200bp, combined                                                                                        |
| `GET /api/v1/risk/limits`           | Bank concentration, liquidity buffer, hedge ratio floors, covenant headroom                                                                              |
| `GET /api/v1/debt/summary`          | Debt, net debt, EBITDA proxy, interest, facilities, covenants                                                                                            |
| `GET /api/v1/debt/facilities`       | Facilities                                                                                                                                               |
| `GET /api/v1/debt/covenants`        | Covenant results                                                                                                                                         |

```json
{
  "facilityId": "FAC-RCF",
  "name": "Minimum interest cover",
  "kind": "MinInterestCover",
  "actual": 4.3614,
  "threshold": 4.0,
  "headroom": 0.0904,
  "status": "Warning"
}
```

Covenant status: `Breach` below the threshold, `Warning` under 10% headroom, otherwise `Pass`.

## In-house bank

| Method and path                                               | Role             | Description                                                                                       |
| ------------------------------------------------------------- | ---------------- | ------------------------------------------------------------------------------------------------- |
| `GET /api/v1/inhouse-bank/balances`                           | any              | Open intercompany balances                                                                        |
| `GET /api/v1/inhouse-bank/netting/preview?settlementCurrency` | any              | Gross volume, net volume, reduction, positions, transfers                                         |
| `POST /api/v1/inhouse-bank/netting/run`                       | Treasurer, Admin | `{ settlementCurrency }`. Records a cycle and clears the balances. 409 if there is nothing to net |
| `GET /api/v1/inhouse-bank/netting/cycles`                     | any              | Completed cycles                                                                                  |

## Payments

| Method and path                      | Role             | Description                                                                                                                                                                    |
| ------------------------------------ | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `GET /api/v1/payments?status`        | any              | Filter by `Draft`, `PendingApproval`, `Approved`, `Released`, `Rejected`                                                                                                       |
| `GET /api/v1/payments/{id}`          | any              | One payment                                                                                                                                                                    |
| `POST /api/v1/payments`              | Treasurer, Admin | `{ accountId, beneficiary, amount, reference, valueDate }` creates a Draft, screened for anomalies and duplicates                                                              |
| `POST /api/v1/payments/{id}/submit`  | creator or Admin | Draft to PendingApproval                                                                                                                                                       |
| `POST /api/v1/payments/{id}/approve` | Approver, Admin  | Not the creator, not twice by the same person                                                                                                                                  |
| `POST /api/v1/payments/{id}/reject`  | Approver, Admin  | `{ reason }` required                                                                                                                                                          |
| `POST /api/v1/payments/{id}/release` | Treasurer, Admin | Must be Approved, releaser must not be an approver, balance net of payments already released through the system must cover the amount. A released payment carries `releasedAt` |

Required approvals: 1, or 2 when the amount in the base currency exceeds `BALLAST_DUAL_APPROVAL_THRESHOLD`, or when
screening flags the payment, or when a duplicate is suspected.

```json
{
  "id": "PAY-1006",
  "accountId": "ACC-US-001",
  "beneficiary": "Shell Trading Co",
  "amount": 1400000,
  "currency": "USD",
  "status": "Draft",
  "createdBy": "U-002",
  "approvals": [],
  "requiredApprovals": 2,
  "rejectionReason": null,
  "riskFlags": [
    "Anomaly screening: Large payment to a beneficiary with no payment history on this account"
  ]
}
```

Releasing marks the payment as sent to the bank. It does not change the account balance, because balances are meant to come
from bank statements.

## Screening, audit and models

| Method and path                 | Description                                                                                        |
| ------------------------------- | -------------------------------------------------------------------------------------------------- |
| `GET /api/v1/anomalies?limit`   | Flagged transactions from the last 30 days plus open flagged payments                              |
| `POST /api/v1/anomalies/assess` | `{ accountId, beneficiary, amount, valueDate }` returns score, threshold, robust z, flags, reasons |
| `GET /api/v1/audit?limit`       | Newest first                                                                                       |
| `GET /api/v1/audit/verify`      | `{ valid, entries, brokenAtSequence }`                                                             |
| `GET /api/v1/ai/models`         | Model catalog from the model service, anomaly baseline, RL episodes, regimes, forecast backtest    |
