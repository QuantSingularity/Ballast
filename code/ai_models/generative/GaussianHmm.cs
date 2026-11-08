using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Generative;

public sealed class GaussianHmm
{
    public double[] Means { get; private set; } = [];
    public double[] Sigmas { get; private set; } = [];
    public double[,] Transition { get; private set; } = new double[0, 0];
    public double[] Initial { get; private set; } = [];

    private static double Emit(double x, double mu, double sigma)
    {
        double z = (x - mu) / sigma;
        return Math.Max(1e-300, Stats.NormalPdf(z) / sigma);
    }

    public static GaussianHmm Fit(IReadOnlyList<double> returns, int iterations = 60)
    {
        int n = returns.Count;
        if (n < 60) throw new ArgumentException("Need at least 60 observations", nameof(returns));
        const int k = 2;
        double sd = Stats.StdDev(returns);
        double mean = Stats.Mean(returns);
        var m = new GaussianHmm
        {
            Means = [mean, mean],
            Sigmas = [sd * 0.6, sd * 1.6],
            Transition = new[,] { { 0.9, 0.1 }, { 0.2, 0.8 } },
            Initial = [0.7, 0.3]
        };
        for (int it = 0; it < iterations; it++)
        {
            var alpha = new double[n, k];
            var scale = new double[n];
            for (int i = 0; i < k; i++) alpha[0, i] = m.Initial[i] * Emit(returns[0], m.Means[i], m.Sigmas[i]);
            scale[0] = alpha[0, 0] + alpha[0, 1];
            for (int i = 0; i < k; i++) alpha[0, i] /= scale[0];
            for (int t = 1; t < n; t++)
            {
                for (int j = 0; j < k; j++)
                {
                    double s = 0;
                    for (int i = 0; i < k; i++) s += alpha[t - 1, i] * m.Transition[i, j];
                    alpha[t, j] = s * Emit(returns[t], m.Means[j], m.Sigmas[j]);
                }
                scale[t] = alpha[t, 0] + alpha[t, 1];
                for (int j = 0; j < k; j++) alpha[t, j] /= scale[t];
            }
            var beta = new double[n, k];
            for (int i = 0; i < k; i++) beta[n - 1, i] = 1;
            for (int t = n - 2; t >= 0; t--)
            {
                for (int i = 0; i < k; i++)
                {
                    double s = 0;
                    for (int j = 0; j < k; j++) s += m.Transition[i, j] * Emit(returns[t + 1], m.Means[j], m.Sigmas[j]) * beta[t + 1, j];
                    beta[t, i] = s / scale[t + 1];
                }
            }
            var gamma = new double[n, k];
            for (int t = 0; t < n; t++)
            {
                double norm = 0;
                for (int i = 0; i < k; i++) { gamma[t, i] = alpha[t, i] * beta[t, i]; norm += gamma[t, i]; }
                for (int i = 0; i < k; i++) gamma[t, i] /= norm;
            }
            var xi = new double[k, k];
            for (int t = 0; t < n - 1; t++)
            {
                double norm = 0;
                var tmp = new double[k, k];
                for (int i = 0; i < k; i++)
                    for (int j = 0; j < k; j++)
                    {
                        tmp[i, j] = alpha[t, i] * m.Transition[i, j] * Emit(returns[t + 1], m.Means[j], m.Sigmas[j]) * beta[t + 1, j];
                        norm += tmp[i, j];
                    }
                for (int i = 0; i < k; i++)
                    for (int j = 0; j < k; j++) xi[i, j] += tmp[i, j] / norm;
            }
            var newMeans = new double[k];
            var newSigmas = new double[k];
            var newTrans = new double[k, k];
            for (int i = 0; i < k; i++)
            {
                double w = 0, wm = 0;
                for (int t = 0; t < n; t++) { w += gamma[t, i]; wm += gamma[t, i] * returns[t]; }
                newMeans[i] = wm / w;
                double wv = 0;
                for (int t = 0; t < n; t++) { double d = returns[t] - newMeans[i]; wv += gamma[t, i] * d * d; }
                newSigmas[i] = Math.Max(1e-6, Math.Sqrt(wv / w));
                double row = 0;
                for (int j = 0; j < k; j++) row += xi[i, j];
                for (int j = 0; j < k; j++) newTrans[i, j] = Math.Max(1e-6, xi[i, j] / row);
                double rs = newTrans[i, 0] + newTrans[i, 1];
                newTrans[i, 0] /= rs;
                newTrans[i, 1] /= rs;
            }
            m.Means = newMeans;
            m.Sigmas = newSigmas;
            m.Transition = newTrans;
            m.Initial = [gamma[0, 0], gamma[0, 1]];
        }
        if (m.Sigmas[0] > m.Sigmas[1])
        {
            m.Means = [m.Means[1], m.Means[0]];
            m.Sigmas = [m.Sigmas[1], m.Sigmas[0]];
            m.Initial = [m.Initial[1], m.Initial[0]];
            m.Transition = new[,] { { m.Transition[1, 1], m.Transition[1, 0] }, { m.Transition[0, 1], m.Transition[0, 0] } };
        }
        return m;
    }

    public double[] FilterLast(IReadOnlyList<double> returns)
    {
        if (returns.Count == 0) return (double[])Initial.Clone();
        var a = new double[2];
        for (int j = 0; j < 2; j++) a[j] = Initial[j] * Emit(returns[0], Means[j], Sigmas[j]);
        Normalize(a);
        for (int t = 1; t < returns.Count; t++)
        {
            var next = new double[2];
            for (int j = 0; j < 2; j++)
                next[j] = (a[0] * Transition[0, j] + a[1] * Transition[1, j]) * Emit(returns[t], Means[j], Sigmas[j]);
            Normalize(next);
            a = next;
        }
        return a;
    }

    private static void Normalize(double[] v)
    {
        double s = v[0] + v[1];
        if (s <= 0) { v[0] = 0.5; v[1] = 0.5; return; }
        v[0] /= s;
        v[1] /= s;
    }
}
