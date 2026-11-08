using Ballast.AiModels.Generative;
using Ballast.AiModels.Research;

namespace Ballast.Risk;

public sealed record FxAtRiskResult(double Var, double ExpectedShortfall, double HighVolProbability, double[] Sigmas, int HorizonDays, double Confidence);

public static class FxAtRisk
{
    public static GaussianHmm Calibrate(IReadOnlyList<double> logReturns) => GaussianHmm.Fit(logReturns);

    public static FxAtRiskResult Compute(GaussianHmm hmm, IReadOnlyList<double> recentReturns, double exposureBase, int horizonDays, double confidence, int seed)
    {
        double pHigh = hmm.FilterLast(recentReturns.TakeLast(20).ToArray())[1];
        var sim = RegimeSwitchingSimulator.FromHmm(hmm, seed);
        var paths = sim.SimulateReturns(3000, horizonDays, pHigh >= 0.5 ? 1 : 0);
        var pnl = paths.Select(p => exposureBase * (Math.Exp(p.Sum()) - 1)).ToArray();
        return new FxAtRiskResult(RiskMetrics.HistoricalVar(pnl, confidence), RiskMetrics.ExpectedShortfall(pnl, confidence),
            pHigh, hmm.Sigmas, horizonDays, confidence);
    }
}
