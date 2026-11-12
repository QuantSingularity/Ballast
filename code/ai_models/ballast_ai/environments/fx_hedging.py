"""Monthly FX hedging environment with two volatility regimes and transaction costs."""

from __future__ import annotations

import random
from dataclasses import dataclass, field

from ballast_ai.config import HEDGE_RATIO_LEVELS

ACTION_COUNT = len(HEDGE_RATIO_LEVELS)
STATE_COUNT = 4 * ACTION_COUNT * 2


def encode_state(time_bucket: int, hedge_index: int, regime: int) -> int:
    return (min(max(time_bucket, 0), 3) * ACTION_COUNT + hedge_index) * 2 + regime


@dataclass(frozen=True)
class FxHedgingConfig:
    steps: int = 4
    sigma: tuple[float, float] = (0.02, 0.05)
    transition: tuple[tuple[float, float], tuple[float, float]] = (
        (0.95, 0.05),
        (0.15, 0.85),
    )
    cost_per_unit: float = 0.004
    risk_aversion: float = 3.0


@dataclass
class StepResult:
    next_state: int
    reward: float
    pnl: float
    done: bool


@dataclass
class FxHedgingEnvironment:
    config: FxHedgingConfig
    seed: int
    _rng: random.Random = field(init=False)
    _t: int = field(init=False, default=0)
    _hedge_idx: int = field(init=False, default=0)
    _regime: int = field(init=False, default=0)

    def __post_init__(self) -> None:
        self._rng = random.Random(self.seed)

    @property
    def state(self) -> int:
        return encode_state(self._t * 4 // self.config.steps, self._hedge_idx, self._regime)

    def reset(self, seed: int | None = None, start_regime: int = 0, start_hedge_index: int = 0) -> int:
        if seed is not None:
            self._rng = random.Random(seed)
        self._t = 0
        self._hedge_idx = start_hedge_index
        self._regime = start_regime
        return self.state

    def step(self, action: int) -> StepResult:
        old_h = HEDGE_RATIO_LEVELS[self._hedge_idx]
        new_h = HEDGE_RATIO_LEVELS[action]
        cost = self.config.cost_per_unit * abs(new_h - old_h)
        sigma = self.config.sigma[self._regime]
        ret = sigma * self._rng.gauss(0.0, 1.0)
        pnl = (1.0 - new_h) * ret - cost
        # Reward uses the expected variance penalty rather than the realized one: realized returns have a
        # standard deviation several times larger than the cost differences the agent has to learn.
        reward = -cost - self.config.risk_aversion * (1.0 - new_h) ** 2 * sigma * sigma
        self._hedge_idx = action
        stay = self.config.transition[self._regime][0]
        self._regime = 0 if self._rng.random() < stay else 1
        self._t += 1
        return StepResult(self.state, reward, pnl, self._t >= self.config.steps)
