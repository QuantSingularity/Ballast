import math

import numpy as np
import pytest

from ballast_ai.numerics import (
    mad,
    mean,
    median,
    normal_inv_cdf,
    quantile,
    std,
    variance,
)


def test_basic_statistics_and_empty_inputs():
    assert mean([1, 2, 3, 4]) == 2.5
    assert median([1, 2, 3, 4]) == 2.5
    assert variance([2, 4, 4, 4, 5, 5, 7, 9], sample=False) == pytest.approx(4.0)
    assert std([1]) == 0.0
    assert mean([]) == 0.0 and quantile([], 0.5) == 0.0 and mad([]) == 0.0


def test_quantile_interpolates_and_clamps():
    assert quantile([0, 10], 0.25) == 2.5
    assert quantile([1, 2, 3], -1) == 1
    assert quantile([1, 2, 3], 2) == 3


def test_mad_is_unscaled_median_absolute_deviation():
    assert mad([1, 1, 2, 2, 4, 6, 9]) == 1.0


def test_inverse_normal():
    assert normal_inv_cdf(0.975) == pytest.approx(1.959964, abs=1e-5)
    assert normal_inv_cdf(0.5) == pytest.approx(0.0, abs=1e-12)
    assert normal_inv_cdf(0) == -math.inf and normal_inv_cdf(1) == math.inf
    assert np.isfinite(normal_inv_cdf(1e-9))
