namespace Ballast.AiModels.Research;

public sealed record ModelInfo(string Name, string Kind, string Version, string Description);

public static class ModelCatalog
{
    public static readonly IReadOnlyList<ModelInfo> Models =
    [
        new("cash-forecast-ensemble", "forecasting", "1.0.0",
            "Damped-trend Holt-Winters blended with a calendar-feature ridge regression; weights from a 28-day holdout; rolling-origin conformal intervals."),
        new("payment-anomaly-detector", "anomaly-detection", "1.0.0",
            "Isolation forest over amount, account-relative size, weekday and beneficiary novelty, combined with per-beneficiary robust z-scores."),
        new("regime-hmm", "regime-detection", "1.0.0",
            "Two-state Gaussian hidden Markov model fitted by Baum-Welch on FX returns; filtered probability of the high-volatility regime."),
        new("fx-hedging-q-agent", "reinforcement-learning", "1.0.0",
            "Tabular Q-learning agent choosing hedge ratios over a monthly, regime-switching FX environment with transaction costs and a mean-variance reward.")
    ];
}
