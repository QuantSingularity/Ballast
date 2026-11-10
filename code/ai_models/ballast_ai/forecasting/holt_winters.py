"""Additive Holt-Winters with a damped trend, fitted by grid search.

All grid candidates are advanced together as numpy vectors, so one fit costs one pass over the series.
"""

from __future__ import annotations

import itertools
from collections.abc import Sequence

import numpy as np

ALPHAS = (0.02, 0.05, 0.1, 0.2, 0.3, 0.5)
BETAS = (0.0, 0.01, 0.05)
GAMMAS = (0.02, 0.05, 0.1, 0.2, 0.4)
_GRID = np.array(list(itertools.product(ALPHAS, BETAS, GAMMAS)), dtype=float)


class HoltWintersAdditive:
    def __init__(
        self,
        season_length: int,
        phi: float,
        alpha: float,
        beta: float,
        gamma: float,
        level: float,
        trend: float,
        season: np.ndarray,
        n: int,
        sse: float,
    ) -> None:
        self.season_length = season_length
        self.phi = phi
        self.alpha = alpha
        self.beta = beta
        self.gamma = gamma
        self._level = level
        self._trend = trend
        self._season = season
        self._n = n
        self.in_sample_sse = sse

    @classmethod
    def fit(
        cls, y: Sequence[float] | np.ndarray, season_length: int = 7, phi: float = 0.9
    ) -> HoltWintersAdditive:
        series = np.asarray(y, dtype=float)
        n, m = series.size, season_length
        if n < 2 * m:
            raise ValueError("Need at least two full seasons of history")
        a, b, g = _GRID[:, 0], _GRID[:, 1], _GRID[:, 2]
        first = float(series[:m].mean())
        second = float(series[m : 2 * m].mean())
        count = _GRID.shape[0]
        level = np.full(count, first)
        trend = np.full(count, (second - first) / m)
        season = np.tile(series[:m] - first, (count, 1))
        sse = np.zeros(count)
        for t in range(m, n):
            si = t % m
            s = season[:, si]
            damped = level + phi * trend
            err = series[t] - (damped + s)
            sse += err * err
            new_level = a * (series[t] - s) + (1.0 - a) * damped
            trend = b * (new_level - level) + (1.0 - b) * phi * trend
            season[:, si] = g * (series[t] - new_level) + (1.0 - g) * s
            level = new_level
        best = int(np.argmin(sse))
        return cls(
            m,
            phi,
            float(a[best]),
            float(b[best]),
            float(g[best]),
            float(level[best]),
            float(trend[best]),
            season[best].copy(),
            n,
            float(sse[best]),
        )

    def forecast(self, horizon: int) -> np.ndarray:
        k = np.arange(1, horizon + 1)
        damp = np.cumsum(self.phi**k)
        seasonal = self._season[(self._n + k - 1) % self.season_length]
        return self._level + damp * self._trend + seasonal
