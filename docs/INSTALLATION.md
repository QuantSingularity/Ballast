# Installation

## Requirements

| Tool               | Version     | Needed for                                                        |
| ------------------ | ----------- | ----------------------------------------------------------------- |
| .NET SDK           | 10          | API, models, mobile UI library, tests                             |
| Node.js            | 22 or newer | Angular app                                                       |
| Docker             | optional    | Container images and compose                                      |
| .NET MAUI workload | optional    | Building the mobile host (`dotnet workload install maui-android`) |

## Steps

```bash
git clone https://github.com/QuantSingularity/Ballast.git
cd Ballast
./scripts/setup_environment.sh      # verifies versions, runs npm ci, writes .env with a generated secret
./scripts/build.sh                  # optional: builds everything
./scripts/start_dev.sh              # API on http://localhost:5000, web on http://localhost:4200
```

Manual equivalent:

```bash
export BALLAST_SECRET_KEY="$(openssl rand -hex 32)"
dotnet run --project code/backend/app/Ballast.Api.csproj --no-launch-profile   # ASPNETCORE_URLS=http://localhost:5000
cd web-frontend && npm ci && npx ng serve                                       # proxies /api to :5000
```

## Verify the install

```bash
./scripts/smoke_test.sh      # publishes and starts the API, signs in, calls 12 endpoints
./scripts/run_tests.sh       # all suites
```

## Docker

```bash
export BALLAST_SECRET_KEY="$(openssl rand -hex 32)"
docker compose -f infrastructure/docker-compose.yml -f infrastructure/docker-compose.override.yml up --build
```

The images have not been built in the environment where this repository was developed. See
[infrastructure/README.md](../infrastructure/README.md).

## Mobile

```bash
dotnet run --project mobile-frontend/Ballast.Mobile.Tests -c Release
```

The shared UI library builds and is tested without any workload. To run the app, install the MAUI workload, point
`ApiAddress.cs` at your API, and follow [the host README](../mobile-frontend/Ballast.Mobile.App/README.md).

## Production notes

Set `BALLAST_ENV=production`, a real `BALLAST_SECRET_KEY`, `BALLAST_CORS_ORIGINS` for your web origin, and
`BALLAST_TRUST_PROXY=true` only if a proxy you control sits in front. Production has no seeded data and no data connectors:
you must supply bank, transaction and market data through your own integration before the screens show anything useful.
