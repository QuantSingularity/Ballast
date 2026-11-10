"""Request and response models. JSON uses camelCase to match the rest of the platform."""

from __future__ import annotations

from datetime import date

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class Api(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class ForecastRequest(Api):
    series: list[float] = Field(min_length=60, max_length=5000)
    start_date: date
    horizon: int = Field(ge=7, le=180)
    confidence: float = Field(default=0.90, ge=0.5, le=0.99)
    origins: int = Field(default=20, ge=0, le=40)


class ForecastResponse(Api):
    dates: list[date]
    point: list[float]
    lower: list[float]
    upper: list[float]
    cum_point: list[float]
    cum_lower: list[float]
    cum_upper: list[float]
    weight_holt_winters: float
    weight_ridge: float
    calibration_origins: int
    confidence: float
    cum_error_samples: list[list[float]]


class BacktestRequest(Api):
    series: list[float] = Field(min_length=200, max_length=5000)
    start_date: date
    horizon: int = Field(default=28, ge=7, le=91)
    outer_origins: int = Field(default=8, ge=1, le=20)
    inner_origins: int = Field(default=20, ge=0, le=40)
    confidence: float = Field(default=0.90, ge=0.5, le=0.99)


class BacktestResponse(Api):
    origins: int
    horizon: int
    mae_ensemble: float
    mae_holt_winters: float
    mae_ridge: float
    mae_seasonal_naive: float
    mase: float
    cumulative_mae_ensemble: float
    cumulative_mae_seasonal_naive: float
    interval_coverage: float
    confidence: float


class OutflowIn(Api):
    account_id: str
    counterparty: str
    amount: float
    date: date


class AnomalyFitRequest(Api):
    records: list[OutflowIn] = Field(min_length=50, max_length=200_000)


class AnomalyFitResponse(Api):
    threshold: float
    samples: int


class AssessItem(Api):
    account_id: str
    counterparty: str
    amount: float
    value_date: date


class AssessBatchRequest(Api):
    items: list[AssessItem] = Field(min_length=1, max_length=20_000)


class AssessResponse(Api):
    score: float
    threshold: float
    robust_z: float
    new_counterparty: bool
    flagged: bool
    reasons: list[str]


class AssessBatchResponse(Api):
    results: list[AssessResponse]


class HmmParams(Api):
    means: list[float] = Field(min_length=2, max_length=2)
    sigmas: list[float] = Field(min_length=2, max_length=2)
    transition: list[list[float]]
    initial: list[float] = Field(min_length=2, max_length=2)


class RegimeFitRequest(Api):
    returns: list[float] = Field(min_length=60, max_length=20_000)


class RegimeFilterRequest(Api):
    params: HmmParams
    returns: list[float] = Field(min_length=1, max_length=20_000)


class RegimeFilterResponse(Api):
    probabilities: list[float]


class FxVarRequest(Api):
    params: HmmParams
    recent_returns: list[float] = Field(min_length=1, max_length=20_000)
    exposure_base: float
    horizon_days: int = Field(default=21, ge=1, le=250)
    confidence: float = Field(default=0.95, ge=0.5, le=0.999)
    seed: int = 31


class FxVarResponse(Api):
    var: float
    expected_shortfall: float
    high_vol_probability: float
    sigmas: list[float]
    horizon_days: int
    confidence: float


class HedgeQuery(Api):
    bucket_index: int = Field(ge=0, le=3)
    current_ratio: float = Field(ge=0, le=2)
    high_vol_probability: float = Field(ge=0, le=1)


class HedgeRecommendRequest(Api):
    queries: list[HedgeQuery] = Field(min_length=1, max_length=500)


class HedgeRecommendResponse(Api):
    ratios: list[float]
    episodes: int


class ModelInfoOut(Api):
    name: str
    kind: str
    version: str
    description: str


class AnomalyStatus(Api):
    fitted: bool
    threshold: float | None
    samples: int


class HedgingStatus(Api):
    episodes: int
    ratio_levels: list[float]
    policy: list[int]


class ModelsResponse(Api):
    version: str
    models: list[ModelInfoOut]
    anomaly: AnomalyStatus
    hedging: HedgingStatus
