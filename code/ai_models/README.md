# AI models (Python)

The `ballast_ai` package holds the platform's models, written in Python with numpy, and a small FastAPI service that the
ASP.NET Core API calls over HTTP. Nothing in this directory is C#.

| Folder                                         | Contents                                                                                                                             |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `ballast_ai/forecasting`                       | Damped Holt-Winters (vectorized grid search), ridge regression, calendar features, ensemble with rolling-origin calibrated intervals |
| `ballast_ai/anomaly`                           | Isolation forest (array-backed trees, vectorized scoring), payment anomaly detector                                                  |
| `ballast_ai/generative`                        | Gaussian hidden Markov model (Baum-Welch), regime-switching simulator                                                                |
| `ballast_ai/environments`, `ballast_ai/agents` | FX hedging environment and tabular Q-learning agent                                                                                  |
| `ballast_ai/research`                          | Rolling-origin backtest, risk metrics and FX VaR, synthetic cash flow generator, model catalog                                       |
| `ballast_ai/service`                           | FastAPI app, request and response schemas, process state (hedging agent, anomaly detector, result cache)                             |
| `examples`, `tests`                            | Runnable examples and the pytest suite                                                                                               |

```bash
python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
.venv/bin/python -m pytest                                  # 41 tests
.venv/bin/python examples/run_examples.py all               # forecast, anomaly, regimes, hedging
.venv/bin/python -m ballast_ai.service --port 8000          # the inference service
.venv/bin/ruff check . && .venv/bin/ruff format --check .
```

The service contract is documented in [docs/MODEL_SERVICE.md](../../docs/MODEL_SERVICE.md). Measured quality and caveats are in
[docs/MODEL_RESULTS.md](../../docs/MODEL_RESULTS.md). The models are trained on synthetic data and are not validated for real
decisions.

This package was ported from an earlier C# implementation. The forecasting code reproduces the C# results exactly on the same
data; the sampling-based models (isolation forest, RL agent, simulator) use numpy and Python random streams, so their exact
values differ slightly.
