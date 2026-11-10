# Contributing

## Workflow

1. Branch from `develop`.
2. Make the change with tests (see below).
3. `./scripts/lint_code.sh --fix`, then `./scripts/run_tests.sh`. Both must pass. Python is formatted and linted with ruff.
4. Open a pull request. CI runs the same scripts.

## Conventions

- Business rules go in `Ballast.Treasury` or `Ballast.Risk` as pure functions with unit tests. Keep API services thin.
- Models go in `code/ai_models/ballast_ai` (Python, numpy), take explicit seeds, and need a pytest that checks behavior (not just that code runs). Vectorize anything that loops over a series: the first version of the forecaster would have needed minutes per backtest.
- A new model capability is exposed by adding a schema and route in `ballast_ai/service`, a method on `AiServiceClient` in `code/backend/ai_client`, and a contract test on both sides.
- Report model quality as measured, including when it is worse than hoped, in `docs/MODEL_RESULTS.md`.
- No new NuGet or npm dependencies without a reason in the pull request. The backend currently needs none.
- Do not weaken a security check to make a test pass. Fix the test or the code.
- Text policy enforced by `lint_code.sh`: no em dashes. Use ASCII punctuation in code comments, documentation and UI copy.

## Adding an endpoint

1. Add request and response records to `code/backend/app/schemas/Dtos.cs`.
2. Add or extend a service in `code/backend/app/services/`, and register it in `ApiHost`.
3. Map the route in `code/backend/app/api/v1/Endpoints.cs` with the right role policy.
4. Add an integration test in `code/backend/tests/ApiTests.cs`, including the role and error cases.
5. Add the TypeScript interface in `web-frontend/src/app/types/models.ts` and the method in `ApiService`.
6. If mobile needs it, add the record in `Ballast.Mobile.UI/Models/Dtos.cs` and a method on `ApiClient`, with a test.
7. Update `docs/API.md` and `docs/FEATURE_MATRIX.md`.

## Tests

The .NET test projects are console programs that use `code/testkit/MiniTest.cs` (attribute `[Fact]`, `Assert.*`) and start the
real Python model service through `AiServiceProcess`. Run one with a name filter:
`dotnet code/backend/tests/bin/Release/net10.0/Ballast.Backend.Tests.dll Payment`. Python tests: `cd code/ai_models &&
.venv/bin/python -m pytest -k forecast`. Web tests use Vitest through `ng test`.
