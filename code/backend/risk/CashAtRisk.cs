using Ballast.AiModels.Numerics;

namespace Ballast.Risk;

public sealed record CashAtRiskResult(int HorizonDays, double Confidence, double CashAtRisk, double ExpectedShortfall, int Samples);

public static class CashAtRiskCalculator
{
    public static CashAtRiskResult Compute(double[][] cumulativeErrorSamples, int horizonDays, double confidence)
    {
        if (cumulativeErrorSamples.Length == 0)
            return new CashAtRiskResult(horizonDays, confidence, 0, 0, 0);
        int idx = Math.Clamp(horizonDays - 1, 0, cumulativeErrorSamples[0].Length - 1);
        var errors = cumulativeErrorSamples.Select(s => s[idx]).ToArray();
        double cutoff = Stats.Quantile(errors, 1 - confidence);
        double car = Math.Max(0, -cutoff);
        var tail = errors.Where(e => e <= cutoff).ToArray();
        double es = tail.Length == 0 ? car : Math.Max(0, -Stats.Mean(tail));
        return new CashAtRiskResult(horizonDays, confidence, car, es, errors.Length);
    }
}
