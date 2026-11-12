"""Tabular Q-learning hedging agent."""

from __future__ import annotations

import random
import statistics
from collections.abc import Callable, Sequence
from dataclasses import dataclass

import numpy as np

from ballast_ai.config import HEDGE_RATIO_LEVELS
from ballast_ai.environments.fx_hedging import (
    ACTION_COUNT,
    STATE_COUNT,
    FxHedgingEnvironment,
    encode_state,
)


@dataclass(frozen=True)
class PolicyEvaluation:
    mean_utility: float
    mean_pnl: float
    pnl_std_dev: float


class QLearningHedger:
    def __init__(self, alpha: float = 0.1, gamma: float = 0.99) -> None:
        self.alpha = alpha
        self.gamma = gamma
        self.q = np.zeros((STATE_COUNT, ACTION_COUNT))
        self.episodes = 0

    def train(self, env: FxHedgingEnvironment, episodes: int, seed: int) -> None:
        rng = random.Random(seed)
        q = self.q.tolist()
        for ep in range(episodes):
            eps = max(0.05, 1.0 - ep / (episodes * 0.8))
            state = env.reset(seed + ep, rng.randrange(2), rng.randrange(ACTION_COUNT))
            done = False
            while not done:
                if rng.random() < eps:
                    action = rng.randrange(ACTION_COUNT)
                else:
                    row = q[state]
                    action = row.index(max(row))
                result = env.step(action)
                target = (
                    result.reward if result.done else result.reward + self.gamma * max(q[result.next_state])
                )
                q[state][action] += self.alpha * (target - q[state][action])
                state = result.next_state
                done = result.done
        self.q = np.asarray(q)
        self.episodes += episodes

    def greedy(self, state: int) -> int:
        return int(np.argmax(self.q[state]))

    def q_value(self, state: int, action: int) -> float:
        return float(self.q[state, action])

    def policy_table(self) -> list[int]:
        return [int(a) for a in np.argmax(self.q, axis=1)]

    @staticmethod
    def evaluate(
        env: FxHedgingEnvironment,
        policy: Callable[[int], int],
        episodes: int,
        seed: int,
    ) -> PolicyEvaluation:
        utilities, pnls = [], []
        for ep in range(episodes):
            state = env.reset(seed + ep, ep % 2)
            u = p = 0.0
            done = False
            while not done:
                result = env.step(policy(state))
                u += result.reward
                p += result.pnl
                state = result.next_state
                done = result.done
            utilities.append(u)
            pnls.append(p)
        return PolicyEvaluation(
            statistics.fmean(utilities),
            statistics.fmean(pnls),
            statistics.stdev(pnls) if len(pnls) > 1 else 0.0,
        )


def recommend_ratios(agent: QLearningHedger, queries: Sequence[tuple[int, float, float]]) -> list[float]:
    """Hedge ratio the policy suggests for (bucket index, current ratio, P(high-volatility regime))."""
    out = []
    for bucket, current, p_high in queries:
        hedge_idx = min(range(ACTION_COUNT), key=lambda i: abs(HEDGE_RATIO_LEVELS[i] - current))
        regime = 1 if p_high >= 0.5 else 0
        out.append(HEDGE_RATIO_LEVELS[agent.greedy(encode_state(bucket, hedge_idx, regime))])
    return out
