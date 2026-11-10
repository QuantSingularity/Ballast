"""Risk measures on P&L samples and FX value at risk from a fitted regime model."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass

import numpy as np

from ballast_ai.generative import GaussianHmm, RegimeSwitchingSimulator
from ballast_ai.numerics import mean, normal_inv_cdf, quantile, std


def historical_var(pnl: Sequence[float] | np.ndarray, confidence: float) -> float:
    return -quantile(pnl, 1.0 - confidence)


def expected_shortfall(pnl: Sequence[float] | np.ndarray, confidence: float) -> float:
    a = np.asarray(pnl, dtype=float)
    cutoff = quantile(a, 1.0 - confidence)
    tail = a[a <= cutoff]
    return float(-cutoff if tail.size == 0 else -tail.mean())


def parametric_var(pnl: Sequence[float] | np.ndarray, confidence: float) -> float:
    return -(mean(pnl) + normal_inv_cdf(1.0 - confidence) * std(pnl))


def ewma_volatility(returns: Sequence[float] | np.ndarray, lam: float = 0.94) -> float:
    a = np.asarray(returns, dtype=float)
    if a.size == 0:
        return 0.0
    v = a[0] ** 2
    for r in a[1:]:
        v = lam * v + (1.0 - lam) * r * r
    return float(np.sqrt(v))


@dataclass(frozen=True)
class FxVarResult:
    var: float
    expected_shortfall: float
    high_vol_probability: float
    sigmas: list[float]
    horizon_days: int
    confidence: float


def fx_var(
    hmm: GaussianHmm,
    recent_returns: Sequence[float],
    exposure_base: float,
    horizon_days: int,
    confidence: float,
    seed: int,
    paths: int = 3000,
) -> FxVarResult:
    p_high = float(hmm.filter_last(np.asarray(recent_returns, dtype=float)[-20:])[1])
    sim = RegimeSwitchingSimulator.from_hmm(hmm, seed)
    simulated = sim.simulate_returns(paths, horizon_days, 1 if p_high >= 0.5 else 0)
    pnl = exposure_base * (np.exp(simulated.sum(axis=1)) - 1.0)
    return FxVarResult(
        historical_var(pnl, confidence),
        expected_shortfall(pnl, confidence),
        p_high,
        hmm.sigmas.tolist(),
        horizon_days,
        confidence,
    )
