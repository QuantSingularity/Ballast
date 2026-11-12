"""Process-wide model state: the trained hedging agent, the fitted anomaly detector and a result cache."""

from __future__ import annotations

import threading
from collections import OrderedDict
from collections.abc import Callable
from typing import Any

from ballast_ai.agents import QLearningHedger
from ballast_ai.anomaly import PaymentAnomalyDetector
from ballast_ai.environments import FxHedgingConfig, FxHedgingEnvironment

AGENT_EPISODES = 25_000
AGENT_SEED = 17
CACHE_SIZE = 64


class ServiceError(Exception):
    def __init__(self, status: int, code: str, message: str) -> None:
        super().__init__(message)
        self.status = status
        self.code = code
        self.message = message


class ServiceState:
    def __init__(self) -> None:
        self._lock = threading.RLock()
        self._agent: QLearningHedger | None = None
        self._detector: PaymentAnomalyDetector | None = None
        self._cache: OrderedDict[str, Any] = OrderedDict()

    @property
    def agent(self) -> QLearningHedger:
        with self._lock:
            if self._agent is None:
                agent = QLearningHedger()
                agent.train(
                    FxHedgingEnvironment(FxHedgingConfig(), AGENT_SEED),
                    AGENT_EPISODES,
                    AGENT_SEED,
                )
                self._agent = agent
            return self._agent

    @property
    def detector(self) -> PaymentAnomalyDetector | None:
        with self._lock:
            return self._detector

    def set_detector(self, detector: PaymentAnomalyDetector) -> None:
        with self._lock:
            self._detector = detector

    def require_detector(self) -> PaymentAnomalyDetector:
        detector = self.detector
        if detector is None or not detector.is_fitted:
            raise ServiceError(409, "model_not_fitted", "The anomaly detector has not been fitted yet")
        return detector

    def cached(self, key: str, compute: Callable[[], Any]) -> Any:
        with self._lock:
            if key in self._cache:
                self._cache.move_to_end(key)
                return self._cache[key]
        value = compute()
        with self._lock:
            self._cache[key] = value
            while len(self._cache) > CACHE_SIZE:
                self._cache.popitem(last=False)
        return value
