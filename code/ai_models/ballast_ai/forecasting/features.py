"""Calendar and lag features for daily cash flow series."""

from __future__ import annotations

import calendar
from collections.abc import Sequence
from datetime import date, timedelta

import numpy as np

MIN_HISTORY = 28
FEATURE_COUNT = 7 + 7 + 2 + 4


def _dom_bucket(dom: int) -> int:
    for bucket, limit in enumerate((5, 10, 15, 20, 25, 28)):
        if dom <= limit:
            return bucket
    return 6


def _calendar_features(d: date) -> list[float]:
    f = [0.0] * 16
    f[(d.weekday() + 1) % 7] = 1.0  # Sunday = 0 ... Saturday = 6
    f[7 + _dom_bucket(d.day)] = 1.0
    f[14] = 1.0 if d.day == calendar.monthrange(d.year, d.month)[1] else 0.0
    f[15] = 1.0 if d.day == 1 else 0.0
    return f


def build_row(d: date, series: Sequence[float], t: int) -> np.ndarray:
    """Features for day index t, using only series[0:t]."""
    f = _calendar_features(d)
    window = series[t - 28 : t]
    f.extend([series[t - 1], series[t - 7], series[t - 14], sum(window) / 28.0])
    return np.asarray(f, dtype=float)


def build_matrix(start: date, y: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Training rows for every t in [MIN_HISTORY, len(y)) and their targets."""
    n = y.size
    ts = np.arange(MIN_HISTORY, n)
    cal = np.array([_calendar_features(start + timedelta(days=int(t))) for t in ts])
    csum = np.concatenate(([0.0], np.cumsum(y)))
    rolling = (csum[ts] - csum[ts - 28]) / 28.0
    lags = np.column_stack([y[ts - 1], y[ts - 7], y[ts - 14], rolling])
    return np.hstack([cal, lags]), y[ts]
