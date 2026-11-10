from datetime import date

import numpy as np
import pytest

from ballast_ai.research import synthetic

START = date(2025, 1, 1)


@pytest.fixture(scope="session")
def start() -> date:
    return START


@pytest.fixture(scope="session")
def series() -> np.ndarray:
    flows = synthetic.generate(START, 540, synthetic.FlowProfile(1.0, 0.0, 1))
    return synthetic.daily_net(flows, START, 540)
