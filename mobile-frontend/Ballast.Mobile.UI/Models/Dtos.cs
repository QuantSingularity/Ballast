namespace Ballast.Mobile.UI.Models;

public sealed record UserInfo(string Id, string Email, string Name, string Role);

public sealed record AuthResponse(UserInfo User, string AccessToken, string RefreshToken, int ExpiresIn);

public sealed record ErrorBody(string Code, string Message);

public sealed record CurrencyPosition(string Currency, decimal Native, decimal Base, decimal Share);

public sealed record BankPosition(string Bank, decimal Base, decimal Share);

public sealed record CashSummary(
    DateOnly AsOf, string BaseCurrency, decimal TotalBase, int AccountCount,
    List<CurrencyPosition> ByCurrency, List<BankPosition> ByBank);

public sealed record LadderBucket(string Label, int Days, double ProjectedBalance, double Lower, double Upper, decimal ScheduledPayments);

public sealed record Ladder(DateOnly AsOf, string BaseCurrency, decimal CurrentBalance, List<LadderBucket> Buckets);

public sealed record ForecastPoint(
    DateOnly Date, double Point, double Lower, double Upper, double Cumulative, double CumulativeLower, double CumulativeUpper);

public sealed record ForecastModel(string Name, double WeightHoltWinters, double WeightRidge, int CalibrationOrigins);

public sealed record Forecast(
    string Scope, string Currency, int HorizonDays, double Confidence, ForecastModel Model, List<ForecastPoint> Daily);

public sealed record PaymentApproval(string UserId, string UserName, DateTimeOffset At);

public sealed record Payment(
    string Id, string AccountId, string Beneficiary, decimal Amount, string Currency, string Reference, DateOnly ValueDate,
    string Status, string CreatedBy, DateTimeOffset CreatedAt, List<PaymentApproval> Approvals, List<string> RiskFlags,
    int RequiredApprovals, string? RejectionReason, DateTimeOffset? ReleasedAt = null);

public sealed record LimitResult(string Id, string Name, decimal Actual, decimal Limit, string Unit, string Status, string Detail);

public sealed record CovenantResult(
    string FacilityId, string FacilityName, string CovenantId, string Name, string Kind, decimal Actual, decimal Threshold,
    decimal Headroom, string Status);

public sealed record ExposureLine(
    string Currency, string Bucket, int BucketIndex, decimal NetNative, decimal NetBase, decimal HedgedNative,
    decimal HedgeRatio, decimal OpenNative, decimal OpenBase);

public sealed record Exposure(DateOnly AsOf, string BaseCurrency, List<ExposureLine> Lines);

public sealed record HedgeRecommendation(
    string Currency, string Bucket, decimal TargetRatio, decimal CurrentRatio, string? Direction, decimal RecommendedNotional,
    double HighVolProbability, string Rationale);

public sealed record AnomalyItem(
    string Source, string Reference, string AccountId, string Counterparty, decimal AmountBase, DateOnly Date, double Score,
    List<string> Reasons);

public sealed record RiskMetrics(DateOnly AsOf, string BaseCurrency, decimal LiquidityBase, decimal LiquidityCoverageRatio);

public sealed record ChartPoint(string Label, double Value, double? Lo = null, double? Hi = null);
