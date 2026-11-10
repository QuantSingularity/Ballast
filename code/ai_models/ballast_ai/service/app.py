"""FastAPI inference service for the Ballast platform."""

from __future__ import annotations

import hashlib
import logging

import numpy as np
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from ballast_ai import __version__
from ballast_ai.agents import recommend_ratios
from ballast_ai.anomaly import AnomalyAssessment, OutflowRecord, PaymentAnomalyDetector
from ballast_ai.config import HEDGE_RATIO_LEVELS
from ballast_ai.forecasting import forecast
from ballast_ai.generative import GaussianHmm
from ballast_ai.research import backtest
from ballast_ai.research.catalog import MODELS
from ballast_ai.research.risk_metrics import fx_var
from ballast_ai.service import schemas as s
from ballast_ai.service.state import ServiceError, ServiceState

log = logging.getLogger("ballast_ai")


def _fingerprint(model) -> str:
    return hashlib.sha256(model.model_dump_json().encode()).hexdigest()


def _assessment(a: AnomalyAssessment) -> s.AssessResponse:
    return s.AssessResponse(
        score=a.score,
        threshold=a.threshold,
        robust_z=a.robust_z,
        new_counterparty=a.new_counterparty,
        flagged=a.flagged,
        reasons=list(a.reasons),
    )


def create_app() -> FastAPI:
    app = FastAPI(
        title="Ballast AI service", version=__version__, docs_url=None, redoc_url=None
    )
    state = ServiceState()
    app.state.models = state

    @app.exception_handler(ServiceError)
    async def service_error(_: Request, exc: ServiceError) -> JSONResponse:
        return JSONResponse(
            status_code=exc.status, content={"code": exc.code, "message": exc.message}
        )

    @app.exception_handler(RequestValidationError)
    async def validation_error(_: Request, exc: RequestValidationError) -> JSONResponse:
        first = exc.errors()[0]
        where = ".".join(str(p) for p in first["loc"] if p != "body")
        return JSONResponse(
            status_code=400,
            content={"code": "validation_error", "message": f"{where}: {first['msg']}"},
        )

    @app.exception_handler(ValueError)
    async def value_error(_: Request, exc: ValueError) -> JSONResponse:
        return JSONResponse(
            status_code=400, content={"code": "validation_error", "message": str(exc)}
        )

    @app.exception_handler(Exception)
    async def unexpected(_: Request, exc: Exception) -> JSONResponse:
        log.exception("Unhandled error", exc_info=exc)
        return JSONResponse(
            status_code=500,
            content={
                "code": "internal_error",
                "message": "An unexpected error occurred",
            },
        )

    @app.get("/health")
    def health() -> dict[str, str]:
        return {"status": "ok", "version": __version__}

    @app.get("/v1/models", response_model=s.ModelsResponse)
    def models() -> s.ModelsResponse:
        agent = state.agent
        detector = state.detector
        fitted = detector is not None and detector.is_fitted
        return s.ModelsResponse(
            version=__version__,
            models=[s.ModelInfoOut(**m.__dict__) for m in MODELS],
            anomaly=s.AnomalyStatus(
                fitted=fitted,
                threshold=detector.threshold if fitted and detector else None,
                samples=detector.samples if detector else 0,
            ),
            hedging=s.HedgingStatus(
                episodes=agent.episodes,
                ratio_levels=list(HEDGE_RATIO_LEVELS),
                policy=agent.policy_table(),
            ),
        )

    @app.post("/v1/forecast", response_model=s.ForecastResponse)
    def forecast_endpoint(req: s.ForecastRequest) -> s.ForecastResponse:
        def compute() -> s.ForecastResponse:
            r = forecast(
                np.asarray(req.series),
                req.start_date,
                req.horizon,
                req.confidence,
                req.origins,
            )
            return s.ForecastResponse(
                dates=r.dates,
                point=r.point.tolist(),
                lower=r.lower.tolist(),
                upper=r.upper.tolist(),
                cum_point=r.cum_point.tolist(),
                cum_lower=r.cum_lower.tolist(),
                cum_upper=r.cum_upper.tolist(),
                weight_holt_winters=r.weight_holt_winters,
                weight_ridge=r.weight_ridge,
                calibration_origins=r.calibration_origins,
                confidence=r.confidence,
                cum_error_samples=r.cum_error_samples.tolist(),
            )

        return state.cached("forecast:" + _fingerprint(req), compute)

    @app.post("/v1/forecast/backtest", response_model=s.BacktestResponse)
    def backtest_endpoint(req: s.BacktestRequest) -> s.BacktestResponse:
        def compute() -> s.BacktestResponse:
            m = backtest.run(
                np.asarray(req.series),
                req.start_date,
                req.horizon,
                req.outer_origins,
                req.inner_origins,
                req.confidence,
            )
            return s.BacktestResponse(**m.__dict__)

        return state.cached("backtest:" + _fingerprint(req), compute)

    @app.post("/v1/anomaly/fit", response_model=s.AnomalyFitResponse)
    def anomaly_fit(req: s.AnomalyFitRequest) -> s.AnomalyFitResponse:
        detector = PaymentAnomalyDetector()
        detector.fit(
            [
                OutflowRecord(r.account_id, r.counterparty, r.amount, r.date)
                for r in req.records
            ]
        )
        state.set_detector(detector)
        return s.AnomalyFitResponse(
            threshold=detector.threshold, samples=detector.samples
        )

    @app.post("/v1/anomaly/assess-batch", response_model=s.AssessBatchResponse)
    def anomaly_assess_batch(req: s.AssessBatchRequest) -> s.AssessBatchResponse:
        detector = state.require_detector()
        results = detector.assess_batch(
            [(i.account_id, i.counterparty, i.amount, i.value_date) for i in req.items]
        )
        return s.AssessBatchResponse(results=[_assessment(a) for a in results])

    @app.post("/v1/regimes/fit", response_model=s.HmmParams)
    def regimes_fit(req: s.RegimeFitRequest) -> s.HmmParams:
        return s.HmmParams(**GaussianHmm.fit(np.asarray(req.returns)).to_dict())

    @app.post("/v1/regimes/filter", response_model=s.RegimeFilterResponse)
    def regimes_filter(req: s.RegimeFilterRequest) -> s.RegimeFilterResponse:
        hmm = GaussianHmm.from_dict(req.params.model_dump())
        return s.RegimeFilterResponse(
            probabilities=hmm.filter_last(np.asarray(req.returns)).tolist()
        )

    @app.post("/v1/risk/fx-var", response_model=s.FxVarResponse)
    def risk_fx_var(req: s.FxVarRequest) -> s.FxVarResponse:
        hmm = GaussianHmm.from_dict(req.params.model_dump())
        r = fx_var(
            hmm,
            req.recent_returns,
            req.exposure_base,
            req.horizon_days,
            req.confidence,
            req.seed,
        )
        return s.FxVarResponse(
            var=r.var,
            expected_shortfall=r.expected_shortfall,
            high_vol_probability=r.high_vol_probability,
            sigmas=r.sigmas,
            horizon_days=r.horizon_days,
            confidence=r.confidence,
        )

    @app.post("/v1/hedging/recommend", response_model=s.HedgeRecommendResponse)
    def hedging_recommend(req: s.HedgeRecommendRequest) -> s.HedgeRecommendResponse:
        agent = state.agent
        ratios = recommend_ratios(
            agent,
            [
                (q.bucket_index, q.current_ratio, q.high_vol_probability)
                for q in req.queries
            ],
        )
        return s.HedgeRecommendResponse(ratios=ratios, episodes=agent.episodes)

    return app
