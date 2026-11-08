# AI models

Dependency-free C# implementations used by the API and runnable on their own.

| Folder                   | Contents                                                                                                    |
| ------------------------ | ----------------------------------------------------------------------------------------------------------- |
| `forecasting`            | Damped Holt-Winters, ridge regression, calendar features, ensemble with rolling-origin calibrated intervals |
| `anomaly`                | Isolation forest, payment anomaly detector                                                                  |
| `generative`             | Gaussian hidden Markov model (Baum-Welch), regime-switching simulator                                       |
| `environments`, `agents` | FX hedging environment and tabular Q-learning agent                                                         |
| `research`               | Backtest harness, risk metrics, synthetic cash flow generator, model catalog                                |
| `numerics`, `config`     | Statistics, Cholesky solver, seeded RNG, defaults                                                           |
| `examples`, `tests`      | Runnable examples and the test suite                                                                        |

```bash
dotnet run --project examples -c Release -- all
dotnet build tests -c Release && dotnet tests/bin/Release/net10.0/Ballast.AiModels.Tests.dll
```

Measured quality and caveats: [docs/MODEL_RESULTS.md](../../docs/MODEL_RESULTS.md). The models are trained on synthetic
data and are not validated for real decisions.
