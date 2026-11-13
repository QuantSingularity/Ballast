from datetime import date

import numpy as np
import pytest
from ballast_ai.anomaly import IsolationForest, OutflowRecord, PaymentAnomalyDetector
from ballast_ai.research import synthetic

START = date(2025, 1, 1)


@pytest.fixture(scope="module")
def detector() -> PaymentAnomalyDetector:
    flows = synthetic.generate(START, 400, synthetic.FlowProfile(1.0, 0.0, 9))
    history = [OutflowRecord("ACC-1", f.counterparty, f.amount, f.date) for f in flows if f.amount < 0]
    d = PaymentAnomalyDetector()
    d.fit(history)
    return d


def test_routine_payment_is_not_flagged(detector):
    a = detector.assess("ACC-1", "Globex Materials", 21_000, date(2026, 3, 10))
    assert not a.flagged, a.reasons


def test_large_payment_to_new_beneficiary_is_flagged(detector):
    a = detector.assess("ACC-1", "Unknown Holdings Ltd", 1_400_000, date(2026, 3, 10))
    assert a.flagged and a.new_counterparty and a.reasons


def test_extreme_amount_scores_higher_than_typical(detector):
    typical = detector.assess("ACC-1", "Globex Materials", 20_000, date(2026, 3, 10))
    extreme = detector.assess("ACC-1", "Globex Materials", 5_000_000, date(2026, 3, 10))
    assert extreme.score > typical.score and extreme.flagged


def test_constant_recurring_payments_are_routine():
    history = [OutflowRecord("A", "Bank Charges", -3500.0, date(2025, 1, 31 - i)) for i in range(12)]
    history += [
        OutflowRecord("A", f"V{i % 5}", -(5000.0 + 50.0 * (i % 7)), date(2025, 2, 1 + i % 20))
        for i in range(120)
    ]
    d = PaymentAnomalyDetector()
    d.fit(history)
    assert not d.assess("A", "Bank Charges", 3500.0, date(2025, 3, 31)).flagged


def test_batch_matches_single_assessments(detector):
    items = [
        ("ACC-1", "Globex Materials", 21_000.0, date(2026, 3, 10)),
        ("ACC-1", "Stark Freight", 400_000.0, date(2026, 3, 11)),
        ("ACC-1", "Brand New LLC", 900_000.0, date(2026, 3, 14)),
    ]
    batch = detector.assess_batch(items)
    for item, b in zip(items, batch, strict=True):
        single = detector.assess(*item)
        assert single.score == pytest.approx(b.score) and single.flagged == b.flagged
    assert any("weekend" in r for r in batch[2].reasons)


def test_unfitted_detector_refuses_and_small_history_is_rejected():
    with pytest.raises(RuntimeError):
        PaymentAnomalyDetector().assess("A", "B", 1.0, date(2026, 1, 5))
    with pytest.raises(ValueError):
        PaymentAnomalyDetector().fit([OutflowRecord("A", "B", -1.0, date(2026, 1, 5))] * 10)


def test_isolation_forest_scores_outliers_higher():
    rng = np.random.default_rng(1)
    data = rng.standard_normal((500, 2))
    forest = IsolationForest()
    forest.fit(data)
    assert forest.score(np.array([8.0, 8.0])) > forest.score(np.array([0.0, 0.0])) + 0.1
    scores = forest.score_batch(data)
    assert scores.min() > 0 and scores.max() < 1


def test_isolation_forest_is_deterministic_for_a_seed():
    data = np.random.default_rng(2).standard_normal((300, 3))
    a, b = IsolationForest(seed=5), IsolationForest(seed=5)
    a.fit(data)
    b.fit(data)
    np.testing.assert_array_equal(a.score_batch(data[:20]), b.score_batch(data[:20]))
