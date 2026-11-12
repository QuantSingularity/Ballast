"""Holt-Winters and ridge blend with rolling-origin conformal intervals."""

from __future__ import annotations

import math
from dataclasses import dataclass
from datetime import date, timedelta

import numpy as np

from ballast_ai.forecasting.features import MIN_HISTORY, build_matrix, build_row
from ballast_ai.forecasting.holt_winters import HoltWintersAdditive
from ballast_ai.forecasting.ridge import RidgeRegressor
from ballast_ai.numerics import normal_inv_cdf, quantile, std


@dataclass
class ForecastResult:
    dates: list[date]
    point: np.ndarray
    lower: np.ndarray
    upper: np.ndarray
    cum_point: np.ndarray
    cum_lower: np.ndarray
    cum_upper: np.ndarray
    weight_holt_winters: float
    weight_ridge: float
    calibration_origins: int
    confidence: float
    cum_error_samples: np.ndarray  # shape (origins, horizon)


class EnsembleModel:
    def __init__(
        self,
        hw: HoltWintersAdditive,
        ridge: RidgeRegressor,
        w_hw: float,
        w_ridge: float,
        start: date,
        history: np.ndarray,
    ) -> None:
        self._hw = hw
        self._ridge = ridge
        self.weight_holt_winters = w_hw
        self.weight_ridge = w_ridge
        self._start = start
        self._history = history

    def predict_holt_winters(self, horizon: int) -> np.ndarray:
        return self._hw.forecast(horizon)

    def predict_ridge(self, horizon: int) -> np.ndarray:
        series = self._history.tolist()
        base = len(series)
        for k in range(horizon):
            t = base + k
            row = build_row(self._start + timedelta(days=t), series, t)
            series.append(float(self._ridge.predict(row)))
        return np.asarray(series[base:], dtype=float)

    def predict(self, horizon: int) -> np.ndarray:
        return self.weight_holt_winters * self.predict_holt_winters(
            horizon
        ) + self.weight_ridge * self.predict_ridge(horizon)


def _fit_components(y: np.ndarray, start: date, w_hw: float, w_ridge: float) -> EnsembleModel:
    hw = HoltWintersAdditive.fit(y)
    x, target = build_matrix(start, y)
    ridge = RidgeRegressor()
    ridge.fit(x, target)
    return EnsembleModel(hw, ridge, w_hw, w_ridge, start, y)


def fit_ensemble(y: np.ndarray, start: date, validation_days: int = 28) -> EnsembleModel:
    y = np.asarray(y, dtype=float)
    if y.size < 60:
        raise ValueError("Need at least 60 days of history")
    v = min(validation_days, y.size // 4)
    probe = _fit_components(y[:-v], start, 0.5, 0.5)
    actual = y[-v:]
    mse_hw = float(np.mean((actual - probe.predict_holt_winters(v)) ** 2)) + 1e-9
    mse_ridge = float(np.mean((actual - probe.predict_ridge(v)) ** 2)) + 1e-9
    w_hw = (1.0 / mse_hw) / (1.0 / mse_hw + 1.0 / mse_ridge)
    return _fit_components(y, start, w_hw, 1.0 - w_hw)


def forecast_with_model(
    y: np.ndarray,
    start: date,
    horizon: int,
    confidence: float = 0.90,
    origins: int = 20,
) -> tuple[ForecastResult, EnsembleModel]:
    y = np.asarray(y, dtype=float)
    model = fit_ensemble(y, start)
    point = model.predict(horizon)

    daily_errors: list[np.ndarray] = []
    cum_errors: list[np.ndarray] = []
    for k in range(origins):
        o = y.size - horizon - 7 * k
        if o < 150:
            break
        pred = fit_ensemble(y[:o], start).predict(horizon)
        err = y[o : o + horizon] - pred
        daily_errors.append(err)
        cum_errors.append(np.cumsum(err))

    cum_point = np.cumsum(point)
    lower = np.empty(horizon)
    upper = np.empty(horizon)
    cum_lower = np.empty(horizon)
    cum_upper = np.empty(horizon)
    tail = (1.0 - confidence) / 2.0
    if len(cum_errors) >= 5:
        de = np.abs(np.vstack(daily_errors))
        ce = np.vstack(cum_errors)
        for h in range(horizon):
            pool = de[:, max(0, h - 3) : min(horizon - 1, h + 3) + 1].ravel()
            level = min(1.0, math.ceil((pool.size + 1) * confidence) / pool.size)
            half = quantile(pool, level)
            cpool = ce[:, max(0, h - 2) : min(horizon - 1, h + 2) + 1].ravel()
            lower[h], upper[h] = point[h] - half, point[h] + half
            cum_lower[h] = cum_point[h] + quantile(cpool, tail)
            cum_upper[h] = cum_point[h] + quantile(cpool, 1.0 - tail)
        samples = ce
    else:
        z = normal_inv_cdf(1.0 - tail)
        sd = std(y)
        for h in range(horizon):
            lower[h], upper[h] = point[h] - z * sd, point[h] + z * sd
            cum_lower[h] = cum_point[h] - z * sd * math.sqrt(h + 1)
            cum_upper[h] = cum_point[h] + z * sd * math.sqrt(h + 1)
        samples = np.zeros((0, horizon))

    dates = [start + timedelta(days=y.size + h) for h in range(horizon)]
    result = ForecastResult(
        dates,
        point,
        lower,
        upper,
        cum_point,
        cum_lower,
        cum_upper,
        model.weight_holt_winters,
        model.weight_ridge,
        len(cum_errors),
        confidence,
        samples,
    )
    return result, model


def forecast(
    y: np.ndarray,
    start: date,
    horizon: int,
    confidence: float = 0.90,
    origins: int = 20,
) -> ForecastResult:
    return forecast_with_model(y, start, horizon, confidence, origins)[0]


__all__ = [
    "MIN_HISTORY",
    "EnsembleModel",
    "ForecastResult",
    "fit_ensemble",
    "forecast",
    "forecast_with_model",
]
