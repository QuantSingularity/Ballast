using Ballast.AiClient;
using Ballast.Core.Domain;
using Ballast.Risk;
using Ballast.Treasury;

namespace Ballast.Api.Schemas;

public sealed record ErrorBody(string Code, string Message);

public sealed record RegisterRequest(string? Email, string? Password, string? Name);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record RoleChangeRequest(UserRole Role);
public sealed record CreateUserRequest(string? Email, string? Name, string? Password, UserRole Role);
public sealed record UserDto(string Id, string Email, string Name, UserRole Role);
public sealed record AuthResponse(UserDto User, string AccessToken, string RefreshToken, int ExpiresIn);

public sealed record CashSummaryDto(
    DateOnly AsOf, string BaseCurrency, decimal TotalBase, int AccountCount,
    IReadOnlyList<CurrencyPosition> ByCurrency, IReadOnlyList<EntityPosition> ByEntity, IReadOnlyList<BankPosition> ByBank);

public sealed record AccountDto(
    string Id, string EntityId, string EntityName, string Bank, string Iban, string Currency,
    decimal Balance, decimal BalanceBase, AccountKind Kind);

public sealed record LadderBucketDto(string Label, int Days, double ProjectedBalance, double Lower, double Upper, decimal ScheduledPayments);

public sealed record LadderDto(DateOnly AsOf, string BaseCurrency, decimal CurrentBalance, IReadOnlyList<LadderBucketDto> Buckets);

public sealed record ForecastModelDto(string Name, double WeightHoltWinters, double WeightRidge, int CalibrationOrigins);

public sealed record ForecastPointDto(
    DateOnly Date, double Point, double Lower, double Upper, double Cumulative, double CumulativeLower, double CumulativeUpper);

public sealed record HistoryPointDto(DateOnly Date, double Net);

public sealed record ForecastDto(
    string Scope, string Currency, int HorizonDays, double Confidence, DateTimeOffset GeneratedAt,
    ForecastModelDto Model, IReadOnlyList<ForecastPointDto> Daily, IReadOnlyList<HistoryPointDto> History);

public sealed record FxRateDto(string Currency, decimal SpotUsd, double InterestRate, decimal Forward30, decimal Forward90, double HighVolProbability);

public sealed record ExposureDto(DateOnly AsOf, string BaseCurrency, IReadOnlyList<ExposureLine> Lines);

public sealed record HedgeDto(
    string Id, string Currency, HedgeDirection Direction, decimal Notional, decimal ContractRate, DateOnly TradeDate,
    DateOnly MaturityDate, string Counterparty, HedgeStatus Status, decimal MarkToMarketBase,
    decimal? EffectivenessRatio, string EffectivenessStatus);

public sealed record CreateHedgeRequest(string? Currency, HedgeDirection Direction, decimal Notional, int TenorDays, string? Counterparty);

public sealed record FxVarDto(string Currency, decimal ExposureBase, double Var95, double ExpectedShortfall95, double HighVolProbability, double[] Sigmas);

public sealed record RiskMetricsDto(
    DateOnly AsOf, string BaseCurrency, IReadOnlyList<CashAtRiskResult> CashAtRisk, decimal LiquidityBase,
    decimal LiquidityCoverageRatio, IReadOnlyList<FxVarDto> FxVar, double FxVarTotal);

public sealed record IntercompanyBalanceDto(
    string Id, string LenderEntityId, string LenderName, string BorrowerEntityId, string BorrowerName, string Currency, decimal Amount);

public sealed record RunNettingRequest(string? SettlementCurrency);

public sealed record FacilityDto(
    string Id, string EntityId, string Name, string Currency, decimal Commitment, decimal Drawn, decimal Undrawn,
    decimal Utilization, decimal AnnualRate, bool Floating, DateOnly Maturity, decimal DrawnBase, decimal UndrawnBase);

public sealed record DebtSummaryDto(
    string BaseCurrency, decimal TotalDebtBase, decimal NetDebtBase, decimal LiquidityBase, decimal EbitdaProxyBase,
    decimal AnnualInterestBase, IReadOnlyList<FacilityDto> Facilities, IReadOnlyList<CovenantResult> Covenants);

public sealed record CreatePaymentRequest(string? AccountId, string? Beneficiary, decimal Amount, string? Reference, DateOnly ValueDate);
public sealed record RejectPaymentRequest(string? Reason);

public sealed record AssessPaymentRequest(string? AccountId, string? Beneficiary, decimal Amount, DateOnly ValueDate);

public sealed record AnomalyItemDto(
    string Source, string Reference, string AccountId, string Counterparty, decimal AmountBase, DateOnly Date,
    double Score, IReadOnlyList<string> Reasons);

public sealed record HmmStatusDto(string Currency, double[] Sigmas, double[] Means, double HighVolProbability, double CalmPersistence, double StressPersistence);

public sealed record AiOverviewDto(
    IReadOnlyList<ModelInfo> Models, double AnomalyThreshold, int HedgingAgentEpisodes,
    IReadOnlyList<HmmStatusDto> Regimes, BacktestMetrics ForecastBacktest);
