namespace Ballast.Risk;

internal static class RiskMath
{
    /// <summary>Linear-interpolation quantile, matching the model service's convention.</summary>
    public static double Quantile(IReadOnlyList<double> values, double q)
    {
        if (values.Count == 0) return 0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        q = Math.Clamp(q, 0, 1);
        double pos = q * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return lo == hi ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }
}
