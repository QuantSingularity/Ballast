# Configuration

The API reads environment variables at startup (`.env` is loaded by `scripts/start_dev.sh`; compose and Kubernetes set them
directly). Invalid combinations fail at startup with an explicit message.

| Variable                          | Default                                  | Description                                                                                                                                                                                      |
| --------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `BALLAST_ENV`                     | `development`                            | `development`, `staging` or `production`. The last two are "production-like"                                                                                                                     |
| `BALLAST_SECRET_KEY`              | none                                     | Signs JWTs. Required, at least 32 characters. Generate: `openssl rand -hex 32`                                                                                                                   |
| `BALLAST_BASE_CURRENCY`           | `USD`                                    | Reporting currency: USD, EUR, GBP, JPY or CHF                                                                                                                                                    |
| `BALLAST_ACCESS_TOKEN_MINUTES`    | `30`                                     | Access token lifetime                                                                                                                                                                            |
| `BALLAST_REFRESH_TOKEN_DAYS`      | `7`                                      | Refresh token lifetime                                                                                                                                                                           |
| `BALLAST_PASSWORD_ITERATIONS`     | `210000`                                 | PBKDF2-SHA256 iterations. At least 100000 in production-like environments                                                                                                                        |
| `BALLAST_AI_SERVICE_URL`          | `http://localhost:8000`                  | Base URL of the Python model service. Must be an absolute http or https URL                                                                                                                      |
| `BALLAST_DATA_PATH`               | unset                                    | Directory for `ballast-state.json`. Unset means state lives in memory only                                                                                                                       |
| `BALLAST_CORS_ORIGINS`            | `http://localhost:4200`                  | Comma-separated allowed origins                                                                                                                                                                  |
| `BALLAST_DUAL_APPROVAL_THRESHOLD` | `50000`                                  | Base-currency amount above which two approvals are required                                                                                                                                      |
| `BALLAST_MAX_BANK_CONCENTRATION`  | `0.35`                                   | Maximum share of group cash at one bank                                                                                                                                                          |
| `BALLAST_MIN_LIQUIDITY_BUFFER`    | `10000000`                               | Minimum cash plus undrawn committed facilities, in the base currency                                                                                                                             |
| `BALLAST_AUTH_RATE_LIMIT`         | `30`                                     | Auth requests per minute per client IP                                                                                                                                                           |
| `BALLAST_TRUST_PROXY`             | `false`                                  | Honor `X-Forwarded-For` and `X-Forwarded-Proto`. Enable only behind a proxy you control, otherwise clients can spoof their IP and bypass the rate limit                                          |
| `BALLAST_ALLOW_SELF_REGISTRATION` | `true` in development, `false` otherwise | Whether anyone can call `POST /api/auth/register`. When off, the very first account can still register (it becomes Admin); after that only an Admin can create users with `POST /api/auth/users` |
| `BALLAST_SEED_DEMO_DATA`          | `true` in development, `false` otherwise | Seed demo entities, accounts, history, users and payments                                                                                                                                        |
| `BALLAST_DEMO_PASSWORD`           | `Ballast-Demo-2026!`                     | Password for seeded users. Production-like environments refuse to seed with the default                                                                                                          |
| `BALLAST_AS_OF`                   | today (UTC)                              | Fixed "today" for deterministic data and tests (`yyyy-MM-dd`)                                                                                                                                    |

## Scripts and tests

| Variable              | Used by                | Description                                                                                                     |
| --------------------- | ---------------------- | --------------------------------------------------------------------------------------------------------------- |
| `BALLAST_AI_PORT`     | `scripts/start_dev.sh` | Port for the model service (default 8000)                                                                       |
| `BALLAST_AI_PYTHON`   | .NET test projects     | Python interpreter used to start the model service in tests. Defaults to `code/ai_models/.venv`, then `python3` |
| `BALLAST_TEST_AI_URL` | .NET test projects     | Use an already running model service instead of starting one                                                    |

## Startup safety checks

- A missing, short or placeholder secret stops the API in every environment. So does a malformed `BALLAST_AI_SERVICE_URL`.
- In `staging` and `production`, demo seeding is off by default; forcing it on with the default demo password is refused.
- In `staging` and `production`, PBKDF2 iterations below 100000 are refused.
- In a production-like environment the first account registered becomes Admin, so register it before exposing the API. After that, self-registration is closed unless `BALLAST_ALLOW_SELF_REGISTRATION=true`.

## Policy parameters not exposed as variables

Hedge policy bands (50-90%, 30-75%, 10-50% by horizon bucket), covenant warning headroom (10%), lockout (5 failures, 5
minutes), stress scenario magnitudes (10% FX shock, 30% collections delay, 200bp) live in code
(`HedgeAdvisor`, `CovenantMonitor`, `AuthService`, `StressTester`). Change them there and add a test.

## Web and mobile

- Web: `web-frontend/proxy.conf.json` proxies `/api` to `http://localhost:5000` during `ng serve`. In containers nginx does
  the same (`infrastructure/docker/nginx.frontend.conf`).
- Mobile: `Ballast.Mobile.App/ApiAddress.cs` chooses the API address. Release builds must use HTTPS.
