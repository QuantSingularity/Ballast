namespace Ballast.AiModels.Numerics;

public static class Stats
{
    public static double Mean(IReadOnlyList<double> x)
    {
        if (x.Count == 0) return 0;
        double s = 0;
        for (int i = 0; i < x.Count; i++) s += x[i];
        return s / x.Count;
    }

    public static double Variance(IReadOnlyList<double> x, bool sample = true)
    {
        int n = x.Count;
        if (n < 2) return 0;
        double m = Mean(x), s = 0;
        for (int i = 0; i < n; i++) { double d = x[i] - m; s += d * d; }
        return s / (sample ? n - 1 : n);
    }

    public static double StdDev(IReadOnlyList<double> x, bool sample = true) => Math.Sqrt(Variance(x, sample));

    public static double Quantile(IReadOnlyList<double> x, double q)
    {
        if (x.Count == 0) return 0;
        var s = x.ToArray();
        Array.Sort(s);
        q = Math.Clamp(q, 0, 1);
        double pos = q * (s.Length - 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return lo == hi ? s[lo] : s[lo] + (s[hi] - s[lo]) * (pos - lo);
    }

    public static double Median(IReadOnlyList<double> x) => Quantile(x, 0.5);

    public static double Mad(IReadOnlyList<double> x)
    {
        if (x.Count == 0) return 0;
        double med = Median(x);
        return Median(x.Select(v => Math.Abs(v - med)).ToArray());
    }

    public static double NormalPdf(double x) => Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);

    public static double NormalInvCdf(double p)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;
        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];
        const double plow = 0.02425, phigh = 1 - plow;
        if (p < plow)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p > phigh)
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        double r = p - 0.5, t = r * r;
        return (((((a[0] * t + a[1]) * t + a[2]) * t + a[3]) * t + a[4]) * t + a[5]) * r / (((((b[0] * t + b[1]) * t + b[2]) * t + b[3]) * t + b[4]) * t + 1);
    }
}

public sealed class Rng(int seed)
{
    private readonly Random _random = new(seed);
    private double? _spare;

    public double NextDouble() => _random.NextDouble();

    public int NextInt(int maxExclusive) => _random.Next(maxExclusive);

    public double NextNormal()
    {
        if (_spare is double s) { _spare = null; return s; }
        double u1 = 1.0 - _random.NextDouble();
        double u2 = _random.NextDouble();
        double mag = Math.Sqrt(-2.0 * Math.Log(u1));
        _spare = mag * Math.Sin(2.0 * Math.PI * u2);
        return mag * Math.Cos(2.0 * Math.PI * u2);
    }
}

public static class LinearAlgebra
{
    public static double[] SolveSpd(double[,] a, double[] b)
    {
        int n = b.Length;
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i, j];
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (sum <= 1e-12) throw new InvalidOperationException("Matrix is not positive definite");
                    l[i, i] = Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
        }
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = b[i];
            for (int k = 0; k < i; k++) sum -= l[i, k] * y[k];
            y[i] = sum / l[i, i];
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = y[i];
            for (int k = i + 1; k < n; k++) sum -= l[k, i] * x[k];
            x[i] = sum / l[i, i];
        }
        return x;
    }
}
