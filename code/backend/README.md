# Backend

ASP.NET Core 10 minimal API organized the way the other full-stack repositories in the organization are (`app`, `core`, `db`,
and domain folders), as separate projects so the dependency direction is enforced by the compiler. See
[docs/architecture.md](../../docs/architecture.md).

```bash
export BALLAST_SECRET_KEY="$(openssl rand -hex 32)" BALLAST_AI_SERVICE_URL=http://127.0.0.1:8000
dotnet run --project app/Ballast.Api.csproj --no-launch-profile      # http://localhost:5000 (needs the model service)
dotnet build tests -c Release && dotnet tests/bin/Release/net10.0/Ballast.Backend.Tests.dll
```

The API has no NuGet dependencies: JWT, password hashing and the helper code are implemented on the .NET shared framework.
The models live in `../ai_models` (Python) and are reached through `ai_client`. The backend tests start a real model service,
so run `scripts/setup_environment.sh` first.
Configuration: [docs/CONFIGURATION.md](../../docs/CONFIGURATION.md). Endpoints: [docs/API.md](../../docs/API.md).
