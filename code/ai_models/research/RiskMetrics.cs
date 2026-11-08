using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Research;

public static class RiskMetrics
{
    public static double HistoricalVar(IReadOnlyList<double> pnl, double confidence) =>
        -Stats.Quantile(pnl, 1 - confidence);

    public static double ExpectedShortfall(IReadOnlyList<double> pnl, double confidence)
    {
        double cutoff = Stats.Quantile(pnl, 1 - confidence);
        var tail = pnl.Where(x => x <= cutoff).ToArray();
        return tail.Length == 0 ? -cutoff : -Stats.Mean(tail);
    }

    public static double ParametricVar(IReadOnlyList<double> pnl, double confidence) =>
        -(Stats.Mean(pnl) + Stats.NormalInvCdf(1 - confidence) * Stats.StdDev(pnl));

    public static double EwmaVolatility(IReadOnlyList<double> returns, double lambda = 0.94)
    {
        if (returns.Count == 0) return 0;
        double v = returns[0] * returns[0];
        for (int i = 1; i < returns.Count; i++) v = lambda * v + (1 - lambda) * returns[i] * returns[i];
        return Math.Sqrt(v);
    }
}
