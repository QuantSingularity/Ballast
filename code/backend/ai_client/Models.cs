namespace Ballast.AiClient;

public sealed record ForecastResult(
    DateOnly[] Dates,
    double[] Point,
    double[] Lower,
    double[] Upper,
    double[] CumPoint,
    double[] CumLower,
    double[] CumUpper,
    double WeightHoltWinters,
    double WeightRidge,
    int CalibrationOrigins,
    double Confidence,
    double[][] CumErrorSamples);

public sealed record BacktestMetrics(
    int Origins,
    int Horizon,
    double MaeEnsemble,
    double MaeHoltWinters,
    double MaeRidge,
    double MaeSeasonalNaive,
    double Mase,
    double CumulativeMaeEnsemble,
    double CumulativeMaeSeasonalNaive,
    double IntervalCoverage,
    double Confidence);

public sealed record OutflowRecord(string AccountId, string Counterparty, double Amount, DateOnly Date);

public sealed record AssessItem(string AccountId, string Counterparty, double Amount, DateOnly ValueDate);

public sealed record AnomalyAssessment(
    double Score, double Threshold, double RobustZ, bool NewCounterparty, bool Flagged, IReadOnlyList<string> Reasons);

public sealed record AnomalyFit(double Threshold, int Samples);

public sealed record HmmParameters(double[] Means, double[] Sigmas, double[][] Transition, double[] Initial);

public sealed record FxVarResult(
    double Var, double ExpectedShortfall, double HighVolProbability, double[] Sigmas, int HorizonDays, double Confidence);

public sealed record HedgeRatioQuery(int BucketIndex, double CurrentRatio, double HighVolProbability);

public sealed record HedgeRecommendations(double[] Ratios, int Episodes);

public sealed record ModelInfo(string Name, string Kind, string Version, string Description);

public sealed record AnomalyStatus(bool Fitted, double? Threshold, int Samples);

public sealed record HedgingStatus(int Episodes, double[] RatioLevels, int[] Policy);

public sealed record ModelStatus(string Version, List<ModelInfo> Models, AnomalyStatus Anomaly, HedgingStatus Hedging);
