import numpy as np
import pytest
from ballast_ai.forecasting import (
    HoltWintersAdditive,
    RidgeRegressor,
    fit_ensemble,
    forecast,
)
from ballast_ai.forecasting.features import FEATURE_COUNT, build_matrix, build_row
from ballast_ai.research import backtest


def test_holt_winters_recovers_weekly_seasonality():
    y = 100 + 20 * np.sin(2 * np.pi * (np.arange(140) % 7) / 7.0)
    f = HoltWintersAdditive.fit(y).forecast(14)
    expected = 100 + 20 * np.sin(2 * np.pi * ((140 + np.arange(14)) % 7) / 7.0)
    assert np.abs(f - expected).max() < 1.0


def test_holt_winters_needs_two_seasons():
    with pytest.raises(ValueError):
        HoltWintersAdditive.fit(np.arange(10.0))


def test_ridge_recovers_linear_relationship():
    rng = np.random.default_rng(3)
    x = rng.standard_normal((400, 2))
    y = 3 * x[:, 0] - 2 * x[:, 1] + 5
    model = RidgeRegressor(0.01)
    model.fit(x, y)
    assert model.predict(np.array([1.0, 2.0])) == pytest.approx(4.0, abs=0.05)
    assert model.predict(x[:3]).shape == (3,)


def test_unfitted_ridge_refuses_to_predict():
    with pytest.raises(RuntimeError):
        RidgeRegressor().predict(np.zeros(3))


def test_vectorized_training_matrix_matches_the_row_builder(start, series):
    x, target = build_matrix(start, series)
    assert x.shape == (series.size - 28, FEATURE_COUNT)
    from datetime import timedelta

    for t in (28, 100, 539):
        row = build_row(start + timedelta(days=t), series.tolist(), t)
        np.testing.assert_allclose(x[t - 28], row)
        assert target[t - 28] == series[t]


def test_ensemble_weights_sum_to_one_and_intervals_bracket_the_point(start, series):
    r = forecast(series, start, 91, 0.9, 12)
    assert r.weight_holt_winters + r.weight_ridge == pytest.approx(1.0)
    assert len(r.point) == 91 and r.calibration_origins >= 5
    assert np.all(r.lower <= r.point) and np.all(r.point <= r.upper)
    assert np.all(r.cum_lower <= r.cum_upper)
    assert r.cum_error_samples.shape == (r.calibration_origins, 91)
    assert r.dates[0] == start.fromordinal(start.toordinal() + 540)


def test_forecast_is_deterministic(start, series):
    a = forecast(series, start, 30, 0.9, 6)
    b = forecast(series, start, 30, 0.9, 6)
    np.testing.assert_array_equal(a.point, b.point)
    np.testing.assert_array_equal(a.cum_lower, b.cum_lower)


def test_ensemble_beats_seasonal_naive_on_synthetic_flows(start, series):
    m = backtest.run(series, start, 28, 6, 8, 0.9)
    assert m.origins >= 4
    assert m.mase < 1.0, f"MASE was {m.mase:.3f}"
    assert m.cumulative_mae_ensemble < m.cumulative_mae_seasonal_naive


def test_calibrated_intervals_cover_most_actuals(start, series):
    m = backtest.run(series, start, 28, 6, 12, 0.9)
    assert m.interval_coverage >= 0.70, f"coverage was {m.interval_coverage:.0%}"


def test_short_history_falls_back_to_a_normal_interval(start):
    y = np.random.default_rng(1).normal(100, 10, 120)
    r = forecast(y, start, 14, 0.9, 20)
    assert r.calibration_origins < 5
    assert np.all(r.cum_lower < r.cum_point) and np.all(r.cum_point < r.cum_upper)


def test_rejects_short_history(start):
    with pytest.raises(ValueError):
        fit_ensemble(np.zeros(30), start)
