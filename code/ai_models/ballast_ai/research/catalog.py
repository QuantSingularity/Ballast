"""Model catalog reported by the service."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class ModelInfo:
    name: str
    kind: str
    version: str
    description: str


MODELS: tuple[ModelInfo, ...] = (
    ModelInfo(
        "cash-forecast-ensemble",
        "forecasting",
        "2.0.0",
        "Damped-trend Holt-Winters blended with a calendar-feature ridge regression; "
        "weights from a 28-day holdout; rolling-origin conformal intervals.",
    ),
    ModelInfo(
        "payment-anomaly-detector",
        "anomaly-detection",
        "2.0.0",
        "Isolation forest over amount, account-relative size, weekday and beneficiary novelty, combined with "
        "per-beneficiary robust z-scores.",
    ),
    ModelInfo(
        "regime-hmm",
        "regime-detection",
        "2.0.0",
        "Two-state Gaussian hidden Markov model fitted by Baum-Welch on FX returns; "
        "filtered probability of the high-volatility regime.",
    ),
    ModelInfo(
        "fx-hedging-q-agent",
        "reinforcement-learning",
        "2.0.0",
        "Tabular Q-learning agent choosing hedge ratios over a monthly, regime-switching FX environment with "
        "transaction costs and a mean-variance reward.",
    ),
)
