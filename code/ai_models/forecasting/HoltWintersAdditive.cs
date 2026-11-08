namespace Ballast.AiModels.Forecasting;

public sealed class HoltWintersAdditive
{
    private readonly int _m;
    private readonly double _phi;
    private readonly double _level;
    private readonly double _trend;
    private readonly double[] _season;
    private readonly int _n;

    public double Alpha { get; }
    public double Beta { get; }
    public double Gamma { get; }
    public double InSampleSse { get; }

    private HoltWintersAdditive(int m, double phi, double alpha, double beta, double gamma, double level, double trend, double[] season, int n, double sse)
    {
        _m = m; _phi = phi; Alpha = alpha; Beta = beta; Gamma = gamma;
        _level = level; _trend = trend; _season = season; _n = n; InSampleSse = sse;
    }

    public static HoltWintersAdditive Fit(IReadOnlyList<double> y, int seasonLength = 7, double phi = 0.9)
    {
        if (y.Count < 2 * seasonLength) throw new ArgumentException("Need at least two full seasons of history", nameof(y));
        double[] alphas = [0.02, 0.05, 0.1, 0.2, 0.3, 0.5];
        double[] betas = [0.0, 0.01, 0.05];
        double[] gammas = [0.02, 0.05, 0.1, 0.2, 0.4];
        HoltWintersAdditive? best = null;
        foreach (var a in alphas)
            foreach (var b in betas)
                foreach (var g in gammas)
                {
                    var candidate = Run(y, seasonLength, phi, a, b, g);
                    if (best is null || candidate.InSampleSse < best.InSampleSse) best = candidate;
                }
        return best!;
    }

    private static HoltWintersAdditive Run(IReadOnlyList<double> y, int m, double phi, double a, double b, double g)
    {
        int n = y.Count;
        double first = 0, second = 0;
        for (int i = 0; i < m; i++) { first += y[i]; second += y[m + i]; }
        first /= m; second /= m;
        double level = first;
        double trend = (second - first) / m;
        var season = new double[m];
        for (int i = 0; i < m; i++) season[i] = y[i] - first;
        double sse = 0;
        for (int t = m; t < n; t++)
        {
            int si = t % m;
            double pred = level + phi * trend + season[si];
            double err = y[t] - pred;
            sse += err * err;
            double newLevel = a * (y[t] - season[si]) + (1 - a) * (level + phi * trend);
            trend = b * (newLevel - level) + (1 - b) * phi * trend;
            season[si] = g * (y[t] - newLevel) + (1 - g) * season[si];
            level = newLevel;
        }
        return new HoltWintersAdditive(m, phi, a, b, g, level, trend, season, n, sse);
    }

    public double[] Forecast(int horizon)
    {
        var result = new double[horizon];
        double damp = 0, p = 1;
        for (int k = 1; k <= horizon; k++)
        {
            p *= _phi;
            damp += p;
            result[k - 1] = _level + damp * _trend + _season[(_n + k - 1) % _m];
        }
        return result;
    }
}
