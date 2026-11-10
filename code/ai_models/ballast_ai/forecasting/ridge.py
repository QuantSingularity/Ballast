"""Ridge regression with standardized features and an unpenalized intercept."""

from __future__ import annotations

import numpy as np


class RidgeRegressor:
    def __init__(self, lam: float = 10.0) -> None:
        self.lam = lam
        self._weights: np.ndarray | None = None
        self._mean = np.zeros(0)
        self._scale = np.ones(0)
        self._intercept = 0.0

    @property
    def is_fitted(self) -> bool:
        return self._weights is not None

    def fit(self, x: np.ndarray, y: np.ndarray) -> None:
        x = np.asarray(x, dtype=float)
        y = np.asarray(y, dtype=float)
        self._mean = x.mean(axis=0)
        sd = x.std(axis=0)
        self._scale = np.where(sd < 1e-9, 1.0, sd)
        z = (x - self._mean) / self._scale
        self._intercept = float(y.mean())
        a = z.T @ z + self.lam * np.eye(z.shape[1])
        self._weights = np.linalg.solve(a, z.T @ (y - self._intercept))

    def predict(self, x: np.ndarray) -> np.ndarray | float:
        if self._weights is None:
            raise RuntimeError("Model is not fitted")
        x = np.asarray(x, dtype=float)
        out = self._intercept + ((x - self._mean) / self._scale) @ self._weights
        return float(out) if np.ndim(out) == 0 else out
