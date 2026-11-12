namespace Ballast.Risk;

/// <summary>
/// Cash at risk and expected shortfall from cumulative forecast errors.
/// </summary>
/// <param name="EffectiveSamples">
/// Approximate number of independent error windows behind the figure. Calibration origins are a few days apart, so
/// windows as long as the horizon overlap heavily and carry far less information than the raw sample count suggests.
/// </param>
/// <param name="Reliability">"low" below 10 effective samples, "moderate" below 30, otherwise "adequate".</param>
/// <param name="Method">"empirical", or "empirical-floored-by-normal" when the empirical tail was too thin to trust alone.</param>
public sealed record CashAtRiskResult(
    int HorizonDays, double Confidence, double CashAtRisk, double ExpectedShortfall, int Samples,
    double EffectiveSamples = 0, string Reliability = "low", string Method = "empirical");

public static class CashAtRiskCalculator
{
    /// <summary>Fewer empirical observations than this in the tail and the estimate is floored by a normal fit.</summary>
    public const int MinTailObservations = 5;

    public static CashAtRiskResult Compute(double[][] cumulativeErrorSamples, int horizonDays, double confidence, int originSpacingDays = 7)
    {
        if (cumulativeErrorSamples.Length == 0)
            return new CashAtRiskResult(horizonDays, confidence, 0, 0, 0, 0, "low", "empirical");
        int idx = Math.Clamp(horizonDays - 1, 0, cumulativeErrorSamples[0].Length - 1);
        var errors = cumulativeErrorSamples.Select(s => s[idx]).ToArray();
        double cutoff = RiskMath.Quantile(errors, 1 - confidence);
        double car = Math.Max(0, -cutoff);
        var tail = errors.Where(e => e <= cutoff).ToArray();
        double es = tail.Length == 0 ? car : Math.Max(0, -tail.Average());
        string method = "empirical";

        if (errors.Length * (1 - confidence) < MinTailObservations)
        {
            // Too few tail points for an empirical quantile (at 99% it is effectively the sample minimum). Take the
            // more conservative of the empirical figure and a normal fit to the same errors.
            double mean = RiskMath.Mean(errors), sd = RiskMath.StdDev(errors);
            double z = RiskMath.NormalInvCdf(1 - confidence);
            double carNormal = Math.Max(0, -(mean + sd * z));
            double esNormal = Math.Max(0, -(mean - sd * RiskMath.NormalPdf(z) / (1 - confidence)));
            car = Math.Max(car, carNormal);
            es = Math.Max(es, esNormal);
            method = "empirical-floored-by-normal";
        }
        es = Math.Max(es, car);

        double effective = Math.Clamp(errors.Length * (double)Math.Max(1, originSpacingDays) / Math.Max(1, horizonDays), 1, errors.Length);
        string reliability = effective < 10 ? "low" : effective < 30 ? "moderate" : "adequate";
        return new CashAtRiskResult(horizonDays, confidence, car, es, errors.Length, Math.Round(effective, 1), reliability, method);
    }
}
