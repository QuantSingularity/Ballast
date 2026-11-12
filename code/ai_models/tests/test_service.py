from datetime import date

import pytest
from fastapi.testclient import TestClient

from ballast_ai.research import synthetic
from ballast_ai.service import create_app

START = date(2025, 1, 1)


@pytest.fixture(scope="module")
def client() -> TestClient:
    return TestClient(create_app())


@pytest.fixture(scope="module")
def series() -> list[float]:
    flows = synthetic.generate(START, 540, synthetic.FlowProfile(1.0, 0.0, 1))
    return synthetic.daily_net(flows, START, 540).tolist()


def test_health(client):
    body = client.get("/health").json()
    assert body["status"] == "ok" and body["version"] == "2.0.0"


def test_forecast_contract_uses_camel_case_and_is_cached(client, series):
    req = {
        "series": series,
        "startDate": "2025-01-01",
        "horizon": 30,
        "confidence": 0.9,
        "origins": 8,
    }
    r = client.post("/v1/forecast", json=req)
    assert r.status_code == 200
    body = r.json()
    assert set(body) == {
        "dates",
        "point",
        "lower",
        "upper",
        "cumPoint",
        "cumLower",
        "cumUpper",
        "weightHoltWinters",
        "weightRidge",
        "calibrationOrigins",
        "confidence",
        "cumErrorSamples",
    }
    assert len(body["point"]) == 30 and body["dates"][0] == "2026-06-25"
    assert len(body["cumErrorSamples"]) == body["calibrationOrigins"]
    assert client.post("/v1/forecast", json=req).json() == body


def test_forecast_validation_errors_use_the_platform_error_shape(client, series):
    r = client.post(
        "/v1/forecast",
        json={"series": [1, 2, 3], "startDate": "2025-01-01", "horizon": 7},
    )
    assert r.status_code == 400 and r.json()["code"] == "validation_error"
    r = client.post("/v1/forecast", json={"series": series, "startDate": "2025-01-01", "horizon": 3})
    assert r.status_code == 400 and "horizon" in r.json()["message"]
    assert client.post("/v1/forecast", json={"series": series, "horizon": 7}).status_code == 400


def test_backtest(client, series):
    r = client.post(
        "/v1/forecast/backtest",
        json={
            "series": series,
            "startDate": "2025-01-01",
            "horizon": 28,
            "outerOrigins": 3,
            "innerOrigins": 8,
        },
    )
    body = r.json()
    assert r.status_code == 200 and body["mase"] < 1.0 and 0 <= body["intervalCoverage"] <= 1
    assert body["origins"] == 3


def _history(n_days=400):
    flows = synthetic.generate(START, n_days, synthetic.FlowProfile(1.0, 0.0, 9))
    return [
        {
            "accountId": "ACC-1",
            "counterparty": f.counterparty,
            "amount": f.amount,
            "date": f.date.isoformat(),
        }
        for f in flows
        if f.amount < 0
    ]


def test_anomaly_requires_fit_then_screens(client):
    app = create_app()
    fresh = TestClient(app)
    items = {
        "items": [
            {
                "accountId": "ACC-1",
                "counterparty": "Globex Materials",
                "amount": 21000,
                "valueDate": "2026-03-10",
            }
        ]
    }
    r = fresh.post("/v1/anomaly/assess-batch", json=items)
    assert r.status_code == 409 and r.json()["code"] == "model_not_fitted"

    fit = fresh.post("/v1/anomaly/fit", json={"records": _history()})
    assert fit.status_code == 200 and fit.json()["samples"] > 500
    batch = {
        "items": items["items"]
        + [
            {
                "accountId": "ACC-1",
                "counterparty": "Unknown Holdings Ltd",
                "amount": 900000,
                "valueDate": "2026-03-10",
            }
        ]
    }
    results = fresh.post("/v1/anomaly/assess-batch", json=batch).json()["results"]
    assert [r["flagged"] for r in results] == [False, True]
    assert results[1]["newCounterparty"] and results[1]["reasons"]
    models = fresh.get("/v1/models").json()
    assert models["anomaly"]["fitted"] and models["anomaly"]["threshold"] == pytest.approx(
        fit.json()["threshold"]
    )


def test_anomaly_fit_needs_enough_records(client):
    r = client.post("/v1/anomaly/fit", json={"records": _history()[:10]})
    assert r.status_code == 400


def test_regime_fit_filter_and_fx_var_round_trip(client):
    import numpy as np

    from ballast_ai.generative import RegimeSwitchingSimulator

    returns = (
        RegimeSwitchingSimulator([0, 0], [0.005, 0.02], [[0.97, 0.03], [0.08, 0.92]], 4)
        .simulate_returns(1, 750)[0]
        .tolist()
    )
    params = client.post("/v1/regimes/fit", json={"returns": returns}).json()
    assert params["sigmas"][0] < params["sigmas"][1] and len(params["transition"]) == 2
    probs = client.post("/v1/regimes/filter", json={"params": params, "returns": returns[-20:]}).json()[
        "probabilities"
    ]
    assert sum(probs) == pytest.approx(1.0)
    var = client.post(
        "/v1/risk/fx-var",
        json={
            "params": params,
            "recentReturns": returns[-20:],
            "exposureBase": 1_000_000,
        },
    ).json()
    assert (
        var["var"] > 0 and var["expectedShortfall"] >= var["var"] and np.isfinite(var["highVolProbability"])
    )
    assert var["horizonDays"] == 21 and var["confidence"] == 0.95


def test_hedging_recommendations_and_model_status(client):
    r = client.post(
        "/v1/hedging/recommend",
        json={
            "queries": [
                {"bucketIndex": 0, "currentRatio": 0.0, "highVolProbability": 0.9},
                {"bucketIndex": 2, "currentRatio": 0.0, "highVolProbability": 0.1},
            ]
        },
    )
    body = r.json()
    assert r.status_code == 200 and body["episodes"] == 25_000 and body["ratios"][0] == 1.0
    assert all(x in (0.0, 0.25, 0.5, 0.75, 1.0) for x in body["ratios"])
    models = client.get("/v1/models").json()
    assert models["hedging"]["episodes"] == 25_000 and len(models["hedging"]["policy"]) == 40
    assert models["models"][0]["name"] == "cash-forecast-ensemble"
    assert client.post("/v1/hedging/recommend", json={"queries": []}).status_code == 400
