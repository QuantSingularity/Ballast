"""Two-state Gaussian hidden Markov model fitted by Baum-Welch (scaled forward-backward, vectorized)."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass

import numpy as np

_SQRT_2PI = np.sqrt(2.0 * np.pi)


def _emission(x: np.ndarray, mu: np.ndarray, sigma: np.ndarray) -> np.ndarray:
    z = (x[:, None] - mu[None, :]) / sigma[None, :]
    return np.maximum(1e-300, np.exp(-0.5 * z * z) / (_SQRT_2PI * sigma[None, :]))


@dataclass
class GaussianHmm:
    means: np.ndarray
    sigmas: np.ndarray
    transition: np.ndarray
    initial: np.ndarray
    iterations_run: int = 0

    @classmethod
    def fit(
        cls,
        returns: Sequence[float] | np.ndarray,
        iterations: int = 60,
        tol: float = 1e-7,
    ) -> GaussianHmm:
        x = np.asarray(returns, dtype=float)
        n = x.size
        if n < 60:
            raise ValueError("Need at least 60 observations")
        sd = float(x.std(ddof=1))
        mu = np.array([x.mean(), x.mean()])
        sigma = np.array([sd * 0.6, sd * 1.6])
        a = np.array([[0.9, 0.1], [0.2, 0.8]])
        pi = np.array([0.7, 0.3])
        prev_ll = -np.inf
        ran = 0
        for _ in range(iterations):
            ran += 1
            b = _emission(x, mu, sigma)
            alpha = np.empty((n, 2))
            scale = np.empty(n)
            v = pi * b[0]
            scale[0] = v.sum()
            alpha[0] = v / scale[0]
            for t in range(1, n):
                v = (alpha[t - 1] @ a) * b[t]
                scale[t] = v.sum()
                alpha[t] = v / scale[t]
            beta = np.empty((n, 2))
            beta[-1] = 1.0
            for t in range(n - 2, -1, -1):
                beta[t] = (a @ (b[t + 1] * beta[t + 1])) / scale[t + 1]
            gamma = alpha * beta
            gamma /= gamma.sum(axis=1, keepdims=True)
            xi = alpha[:-1, :, None] * a[None, :, :] * (b[1:] * beta[1:])[:, None, :]
            xi /= xi.sum(axis=(1, 2), keepdims=True)
            xi_sum = xi.sum(axis=0)
            ll = float(np.log(scale).sum())

            w = gamma.sum(axis=0)
            mu = (gamma * x[:, None]).sum(axis=0) / w
            var = (gamma * (x[:, None] - mu[None, :]) ** 2).sum(axis=0) / w
            sigma = np.maximum(1e-6, np.sqrt(var))
            a = np.maximum(1e-6, xi_sum / xi_sum.sum(axis=1, keepdims=True))
            a /= a.sum(axis=1, keepdims=True)
            pi = gamma[0]
            if ll - prev_ll < tol:
                break
            prev_ll = ll

        order = np.argsort(sigma)
        return cls(mu[order], sigma[order], a[np.ix_(order, order)], pi[order], ran)

    def filter_last(self, returns: Sequence[float] | np.ndarray) -> np.ndarray:
        """Filtered state probabilities after the last observation."""
        x = np.asarray(returns, dtype=float)
        if x.size == 0:
            return self.initial.copy()
        b = _emission(x, self.means, self.sigmas)
        alpha = self.initial * b[0]
        alpha = self._normalize(alpha)
        for t in range(1, x.size):
            alpha = self._normalize((alpha @ self.transition) * b[t])
        return alpha

    @staticmethod
    def _normalize(v: np.ndarray) -> np.ndarray:
        s = v.sum()
        return np.array([0.5, 0.5]) if s <= 0 else v / s

    def to_dict(self) -> dict[str, list]:
        return {
            "means": self.means.tolist(),
            "sigmas": self.sigmas.tolist(),
            "transition": self.transition.tolist(),
            "initial": self.initial.tolist(),
        }

    @classmethod
    def from_dict(cls, d: dict) -> GaussianHmm:
        return cls(
            np.asarray(d["means"], dtype=float),
            np.asarray(d["sigmas"], dtype=float),
            np.asarray(d["transition"], dtype=float),
            np.asarray(d["initial"], dtype=float),
        )
