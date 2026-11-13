"""Regime-switching return simulator."""

from __future__ import annotations

import numpy as np
from ballast_ai.generative.hmm import GaussianHmm


class RegimeSwitchingSimulator:
    def __init__(self, means, sigmas, transition, seed: int) -> None:
        self.means = np.asarray(means, dtype=float)
        self.sigmas = np.asarray(sigmas, dtype=float)
        self.transition = np.asarray(transition, dtype=float)
        self.seed = seed

    @classmethod
    def from_hmm(cls, hmm: GaussianHmm, seed: int) -> RegimeSwitchingSimulator:
        return cls(hmm.means, hmm.sigmas, hmm.transition, seed)

    def simulate_returns(self, paths: int, steps: int, start_regime: int = 0) -> np.ndarray:
        rng = np.random.default_rng(self.seed)
        regime = np.full(paths, start_regime, dtype=np.int64)
        out = np.empty((paths, steps))
        for t in range(steps):
            out[:, t] = self.means[regime] + self.sigmas[regime] * rng.standard_normal(paths)
            regime = np.where(rng.random(paths) < self.transition[regime, 0], 0, 1)
        return out
