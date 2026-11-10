"""Deterministic synthetic cash flows with weekly, month-end, payroll, rent and quarterly tax structure."""

from __future__ import annotations

import calendar
import math
import random
from dataclasses import dataclass
from datetime import date, timedelta

import numpy as np

CUSTOMERS = (
    "Northwind Retail",
    "Contoso Wholesale",
    "Fabrikam Industrial",
    "Adventure Works",
    "Tailspin Logistics",
    "Wingtip Online",
)
SUPPLIERS = (
    "Globex Materials",
    "Initech Components",
    "Umbrella Packaging",
    "Stark Freight",
    "Wayne Utilities",
    "Hooli Cloud",
)


@dataclass(frozen=True)
class SyntheticFlow:
    date: date
    category: str
    counterparty: str
    amount: float


@dataclass(frozen=True)
class FlowProfile:
    scale: float = 1.0
    net_drift: float = 0.0
    seed: int = 1


def _is_weekend(d: date) -> bool:
    return d.weekday() >= 5


def _roll_back(d: date) -> date:
    while _is_weekend(d):
        d -= timedelta(days=1)
    return d


def _roll_forward(d: date) -> date:
    while _is_weekend(d):
        d += timedelta(days=1)
    return d


def generate(start: date, days: int, profile: FlowProfile) -> list[SyntheticFlow]:
    rng = random.Random(profile.seed)
    flows: list[SyntheticFlow] = []
    scale = profile.scale
    for i in range(days):
        d = start + timedelta(days=i)
        dim = calendar.monthrange(d.year, d.month)[1]
        if not _is_weekend(d):
            boost = 1.35 if d.day >= 26 else 1.0
            for _ in range(2 + rng.randrange(3)):
                amt = (
                    scale
                    * 25_000
                    * math.exp(0.5 * rng.gauss(0, 1))
                    * boost
                    * (1 + profile.net_drift)
                )
                flows.append(SyntheticFlow(d, "Receipts", rng.choice(CUSTOMERS), amt))
            mid = 1.2 if 10 <= d.day <= 20 else 1.0
            for _ in range(1 + rng.randrange(3)):
                amt = -scale * 21_000 * math.exp(0.45 * rng.gauss(0, 1)) * mid
                flows.append(SyntheticFlow(d, "Suppliers", rng.choice(SUPPLIERS), amt))
        if d == _roll_back(date(d.year, d.month, min(25, dim))):
            flows.append(
                SyntheticFlow(
                    d,
                    "Payroll",
                    "Payroll Run",
                    -scale * 180_000 * (1 + 0.02 * rng.gauss(0, 1)),
                )
            )
        if d == _roll_forward(date(d.year, d.month, 1)):
            flows.append(SyntheticFlow(d, "Rent", "Property Holdings", -scale * 45_000))
        if d.month % 3 == 0 and d == _roll_forward(date(d.year, d.month, 15)):
            flows.append(
                SyntheticFlow(
                    d,
                    "Tax",
                    "Revenue Authority",
                    -scale * 120_000 * (1 + 0.1 * rng.gauss(0, 1)),
                )
            )
        if d.day == dim:
            flows.append(SyntheticFlow(d, "Fees", "Bank Charges", -scale * 3_500))
    return flows


def daily_net(flows: list[SyntheticFlow], start: date, days: int) -> np.ndarray:
    y = np.zeros(days)
    for f in flows:
        idx = (f.date - start).days
        if 0 <= idx < days:
            y[idx] += f.amount
    return y
