"""Isolation forest with array-backed trees so that scoring is vectorized over samples."""

from __future__ import annotations

import math

import numpy as np

_EULER = 0.5772156649


def _c(n: np.ndarray | int) -> np.ndarray:
    """Average path length of an unsuccessful binary search tree lookup."""
    n = np.asarray(n, dtype=float)
    safe = np.maximum(n, 3.0)
    value = 2.0 * (np.log(safe - 1.0) + _EULER) - 2.0 * (safe - 1.0) / safe
    return np.where(n <= 1.0, 0.0, np.where(n == 2.0, 1.0, value))


class _Tree:
    __slots__ = ("feature", "left", "right", "size", "split")

    def __init__(self) -> None:
        self.feature: list[int] = []
        self.split: list[float] = []
        self.left: list[int] = []
        self.right: list[int] = []
        self.size: list[int] = []

    def add_leaf(self, size: int) -> int:
        return self._add(0, 0.0, -1, -1, size)

    def _add(self, feature: int, split: float, left: int, right: int, size: int) -> int:
        self.feature.append(feature)
        self.split.append(split)
        self.left.append(left)
        self.right.append(right)
        self.size.append(size)
        return len(self.feature) - 1

    def add_internal(self, feature: int, split: float, size: int) -> int:
        return self._add(feature, split, -1, -1, size)

    def freeze(self) -> tuple[np.ndarray, ...]:
        return (
            np.asarray(self.feature, dtype=np.int64),
            np.asarray(self.split, dtype=float),
            np.asarray(self.left, dtype=np.int64),
            np.asarray(self.right, dtype=np.int64),
            np.asarray(self.size, dtype=float),
        )


class IsolationForest:
    def __init__(self, trees: int = 100, sample_size: int = 256, seed: int = 7) -> None:
        self.trees = trees
        self.sample_size = sample_size
        self.seed = seed
        self._frozen: list[tuple[np.ndarray, ...]] = []
        self._psi = 0
        self._limit = 0

    def fit(self, data: np.ndarray) -> None:
        data = np.asarray(data, dtype=float)
        if data.shape[0] < 8:
            raise ValueError("Need at least 8 samples")
        rng = np.random.default_rng(self.seed)
        self._psi = min(self.sample_size, data.shape[0])
        self._limit = math.ceil(math.log2(self._psi))
        self._frozen = []
        for _ in range(self.trees):
            sample = data[rng.integers(0, data.shape[0], self._psi)]
            tree = _Tree()
            self._build(tree, sample, 0, rng)
            self._frozen.append(tree.freeze())

    def _build(self, tree: _Tree, rows: np.ndarray, depth: int, rng: np.random.Generator) -> int:
        count = rows.shape[0]
        if depth >= self._limit or count <= 1:
            return tree.add_leaf(count)
        dims = rows.shape[1]
        for _ in range(dims):
            f = int(rng.integers(0, dims))
            col = rows[:, f]
            lo, hi = float(col.min()), float(col.max())
            if hi - lo < 1e-12:
                continue
            split = lo + float(rng.random()) * (hi - lo)
            mask = col < split
            if not mask.any() or mask.all():
                continue
            node = tree.add_internal(f, split, count)
            tree.left[node] = self._build(tree, rows[mask], depth + 1, rng)
            tree.right[node] = self._build(tree, rows[~mask], depth + 1, rng)
            return node
        return tree.add_leaf(count)

    def score_batch(self, x: np.ndarray) -> np.ndarray:
        if not self._frozen:
            raise RuntimeError("Model is not fitted")
        x = np.atleast_2d(np.asarray(x, dtype=float))
        m = x.shape[0]
        rows = np.arange(m)
        total = np.zeros(m)
        for feature, split, left, right, size in self._frozen:
            node = np.zeros(m, dtype=np.int64)
            depth = np.zeros(m)
            for _ in range(self._limit + 1):
                internal = left[node] >= 0
                if not internal.any():
                    break
                go_left = x[rows, feature[node]] < split[node]
                nxt = np.where(go_left, left[node], right[node])
                node = np.where(internal, nxt, node)
                depth += internal
            total += depth + _c(size[node])
        avg = total / len(self._frozen)
        return np.power(2.0, -avg / float(_c(self._psi)))

    def score(self, x: np.ndarray) -> float:
        return float(self.score_batch(np.asarray(x, dtype=float)[None, :])[0])
