using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Forecasting;

public sealed class RidgeRegressor(double lambda = 10.0)
{
    private double[] _weights = [];
    private double[] _mean = [];
    private double[] _scale = [];
    private double _intercept;

    public bool IsFitted => _weights.Length > 0;

    public void Fit(IReadOnlyList<double[]> x, IReadOnlyList<double> y)
    {
        int n = x.Count, p = x[0].Length;
        _mean = new double[p];
        _scale = new double[p];
        for (int j = 0; j < p; j++)
        {
            double m = 0;
            for (int i = 0; i < n; i++) m += x[i][j];
            m /= n;
            double v = 0;
            for (int i = 0; i < n; i++) { double d = x[i][j] - m; v += d * d; }
            double sd = Math.Sqrt(v / n);
            _mean[j] = m;
            _scale[j] = sd < 1e-9 ? 1.0 : sd;
        }
        _intercept = Stats.Mean(y);
        var a = new double[p, p];
        var rhs = new double[p];
        var z = new double[p];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < p; j++) z[j] = (x[i][j] - _mean[j]) / _scale[j];
            double yc = y[i] - _intercept;
            for (int j = 0; j < p; j++)
            {
                rhs[j] += z[j] * yc;
                for (int k = 0; k <= j; k++) a[j, k] += z[j] * z[k];
            }
        }
        for (int j = 0; j < p; j++)
        {
            for (int k = 0; k < j; k++) a[k, j] = a[j, k];
            a[j, j] += lambda;
        }
        _weights = LinearAlgebra.SolveSpd(a, rhs);
    }

    public double Predict(double[] x)
    {
        double s = _intercept;
        for (int j = 0; j < _weights.Length; j++) s += _weights[j] * (x[j] - _mean[j]) / _scale[j];
        return s;
    }
}
