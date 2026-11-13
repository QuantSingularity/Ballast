"""Rolling-origin backtest of the cash forecast against its components and a seasonal-naive baseline."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date

import numpy as np
from ballast_ai.forecasting.ensemble import forecast_with_model


@dataclass(frozen=True)
class BacktestMetrics:
    origins: int
    horizon: int
    mae_ensemble: float
    mae_holt_winters: float
    mae_ridge: float
    mae_seasonal_naive: float
    mase: float
    cumulative_mae_ensemble: float
    cumulative_mae_seasonal_naive: float
    interval_coverage: float
    confidence: float


def run(
    y: np.ndarray,
    start: date,
    horizon: int,
    outer_origins: int,
    inner_origins: int,
    confidence: float = 0.90,
) -> BacktestMetrics:
    y = np.asarray(y, dtype=float)
    e_ens = e_hw = e_ridge = e_naive = c_ens = c_naive = 0.0
    covered = total = used = 0
    for j in range(outer_origins):
        o = y.size - horizon - 14 * j
        if o < 200:
            break
        history = y[:o]
        result, model = forecast_with_model(history, start, horizon, confidence, inner_origins)
        hw = model.predict_holt_winters(horizon)
        ridge = model.predict_ridge(horizon)
        actual = y[o : o + horizon]
        naive = history[o - 7 + (np.arange(horizon) % 7)]
        e_ens += float(np.abs(actual - result.point).sum())
        e_hw += float(np.abs(actual - hw).sum())
        e_ridge += float(np.abs(actual - ridge).sum())
        e_naive += float(np.abs(actual - naive).sum())
        cum_actual = np.cumsum(actual)
        cum_naive = np.cumsum(naive)
        inside = (cum_actual >= result.cum_lower) & (cum_actual <= result.cum_upper)
        covered += int(inside.sum())
        total += horizon
        c_ens += abs(float(cum_actual[-1] - result.cum_point[-1]))
        c_naive += abs(float(cum_actual[-1] - cum_naive[-1]))
        used += 1
    n = max(1, used * horizon)
    return BacktestMetrics(
        used,
        horizon,
        e_ens / n,
        e_hw / n,
        e_ridge / n,
        e_naive / n,
        e_ens / e_naive if e_naive > 0 else 0.0,
        c_ens / max(1, used),
        c_naive / max(1, used),
        covered / total if total else 0.0,
        confidence,
    )
