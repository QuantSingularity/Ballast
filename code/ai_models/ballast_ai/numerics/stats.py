"""Small statistics helpers with the same conventions as the rest of the platform."""

from __future__ import annotations

import math
from collections.abc import Sequence
from statistics import NormalDist

import numpy as np

_NORMAL = NormalDist()


def _arr(x: Sequence[float] | np.ndarray) -> np.ndarray:
    return np.asarray(x, dtype=float)


def mean(x: Sequence[float] | np.ndarray) -> float:
    a = _arr(x)
    return float(a.mean()) if a.size else 0.0


def variance(x: Sequence[float] | np.ndarray, sample: bool = True) -> float:
    a = _arr(x)
    if a.size < 2:
        return 0.0
    return float(a.var(ddof=1 if sample else 0))


def std(x: Sequence[float] | np.ndarray, sample: bool = True) -> float:
    return math.sqrt(variance(x, sample))


def quantile(x: Sequence[float] | np.ndarray, q: float) -> float:
    """Linear-interpolation quantile; 0 for empty input; q is clamped to [0, 1]."""
    a = _arr(x)
    if a.size == 0:
        return 0.0
    return float(np.quantile(a, min(max(q, 0.0), 1.0)))


def median(x: Sequence[float] | np.ndarray) -> float:
    return quantile(x, 0.5)


def mad(x: Sequence[float] | np.ndarray) -> float:
    """Median absolute deviation (unscaled)."""
    a = _arr(x)
    if a.size == 0:
        return 0.0
    return median(np.abs(a - median(a)))


def normal_pdf(x: float) -> float:
    return _NORMAL.pdf(x)


def normal_inv_cdf(p: float) -> float:
    if p <= 0.0:
        return -math.inf
    if p >= 1.0:
        return math.inf
    return _NORMAL.inv_cdf(p)
