"""Payment anomaly screening: isolation forest plus per-beneficiary robust z-scores."""

from __future__ import annotations

import math
from collections import defaultdict
from collections.abc import Sequence
from dataclasses import dataclass, field
from datetime import date

import numpy as np
from ballast_ai.anomaly.isolation_forest import IsolationForest
from ballast_ai.numerics import mad, median, quantile


@dataclass(frozen=True)
class OutflowRecord:
    account_id: str
    counterparty: str
    amount: float
    date: date


@dataclass(frozen=True)
class AnomalyAssessment:
    score: float
    threshold: float
    robust_z: float
    new_counterparty: bool
    flagged: bool
    reasons: list[str] = field(default_factory=list)


def _key(account: str, counterparty: str) -> str:
    return f"{account}|{counterparty}"


class PaymentAnomalyDetector:
    def __init__(self) -> None:
        self._forest = IsolationForest(trees=120, sample_size=256, seed=11)
        self._account_median: dict[str, float] = {}
        self._counterparty: dict[str, tuple[float, float, int]] = {}
        self._known: set[str] = set()
        self._global_median = 1.0
        self.threshold = 1.0
        self.is_fitted = False
        self.samples = 0

    def fit(self, history: Sequence[OutflowRecord]) -> None:
        if len(history) < 50:
            raise ValueError("Need at least 50 outflows")
        by_account: dict[str, list[float]] = defaultdict(list)
        by_cp: dict[str, list[float]] = defaultdict(list)
        for r in history:
            amount = abs(r.amount)
            by_account[r.account_id].append(amount)
            by_cp[_key(r.account_id, r.counterparty)].append(amount)
        self._account_median = {k: max(1.0, median(v)) for k, v in by_account.items()}
        self._global_median = max(1.0, median([abs(r.amount) for r in history]))
        self._counterparty = {}
        for k, amounts in by_cp.items():
            med = median(amounts)
            self._counterparty[k] = (
                med,
                max(max(1.0, 0.02 * med), mad(amounts)),
                len(amounts),
            )
        self._known = set(by_cp)
        rows = np.array(
            [self._features(r.account_id, abs(r.amount), r.date, new_cp=False) for r in history],
            dtype=float,
        )
        self._forest.fit(rows)
        self.threshold = quantile(self._forest.score_batch(rows), 0.995)
        self.samples = len(history)
        self.is_fitted = True

    def _features(self, account: str, amount: float, d: date, new_cp: bool) -> list[float]:
        med = self._account_median.get(account, self._global_median)
        return [
            math.log(1.0 + amount),
            math.log((1.0 + amount) / (1.0 + med)),
            1.0 if d.weekday() >= 5 else 0.0,
            1.0 if new_cp else 0.0,
            d.day / 31.0,
        ]

    def assess(
        self, account_id: str, counterparty: str, amount: float, value_date: date
    ) -> AnomalyAssessment:
        return self.assess_batch([(account_id, counterparty, amount, value_date)])[0]

    def assess_batch(self, items: Sequence[tuple[str, str, float, date]]) -> list[AnomalyAssessment]:
        if not self.is_fitted:
            raise RuntimeError("Detector is not fitted")
        prepared = []
        for account, cp, amount, d in items:
            amount = abs(amount)
            is_new = _key(account, cp) not in self._known
            prepared.append((account, cp, amount, d, is_new))
        feats = np.array(
            [self._features(a, amt, d, new) for a, _, amt, d, new in prepared],
            dtype=float,
        )
        scores = self._forest.score_batch(feats) if len(prepared) else np.zeros(0)
        return [self._decide(p, float(s)) for p, s in zip(prepared, scores, strict=True)]

    def _decide(self, p: tuple[str, str, float, date, bool], score: float) -> AnomalyAssessment:
        account, cp, amount, d, is_new = p
        z = 0.0
        routine = False
        reasons: list[str] = []
        stat = self._counterparty.get(_key(account, cp))
        if stat is not None and stat[2] >= 5:
            z = 0.6745 * (amount - stat[0]) / stat[1]
            routine = abs(z) <= 3
            if abs(z) > 6:
                reasons.append(f"Amount deviates from this beneficiary's usual payments (robust z = {z:.1f})")
        med = self._account_median.get(account, self._global_median)
        big_new = is_new and amount > 3 * med
        if big_new:
            reasons.append("Large payment to a beneficiary with no payment history on this account")
        if score > self.threshold and not routine:
            reasons.append(f"Isolation score {score:.2f} exceeds the {self.threshold:.2f} baseline")
        if d.weekday() >= 5:
            reasons.append("Value date falls on a weekend")
        flagged = bool(reasons) and ((score > self.threshold and not routine) or abs(z) > 6 or big_new)
        return AnomalyAssessment(score, self.threshold, z, is_new, flagged, reasons)
