using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Forecasting;

public sealed record ForecastResult(
    DateOnly[] Dates,
    double[] Point,
    double[] Lower,
    double[] Upper,
    double[] CumPoint,
    double[] CumLower,
    double[] CumUpper,
    double WeightHoltWinters,
    double WeightRidge,
    int CalibrationOrigins,
    double Confidence,
    double[][] CumErrorSamples);

public sealed class EnsembleModel(
    HoltWintersAdditive hw, RidgeRegressor ridge, double wHw, double wRidge, DateOnly start, double[] history)
{
    public double WeightHoltWinters { get; } = wHw;
    public double WeightRidge { get; } = wRidge;

    public double[] PredictHoltWinters(int horizon) => hw.Forecast(horizon);

    public double[] PredictRidge(int horizon)
    {
        var series = new List<double>(history);
        for (int k = 0; k < horizon; k++)
        {
            int t = series.Count;
            series.Add(ridge.Predict(CashFlowFeatures.Build(start.AddDays(t), series, t)));
        }
        return series.GetRange(history.Length, horizon).ToArray();
    }

    public double[] Predict(int horizon)
    {
        var a = PredictHoltWinters(horizon);
        var b = PredictRidge(horizon);
        var r = new double[horizon];
        for (int i = 0; i < horizon; i++) r[i] = WeightHoltWinters * a[i] + WeightRidge * b[i];
        return r;
    }
}

public static class EnsembleCashForecaster
{
    private static EnsembleModel FitComponents(double[] y, DateOnly start, double wHw, double wRidge)
    {
        var hw = HoltWintersAdditive.Fit(y);
        var rows = new List<double[]>();
        var targets = new List<double>();
        for (int t = CashFlowFeatures.MinHistory; t < y.Length; t++)
        {
            rows.Add(CashFlowFeatures.Build(start.AddDays(t), y, t));
            targets.Add(y[t]);
        }
        var ridge = new RidgeRegressor();
        ridge.Fit(rows, targets);
        return new EnsembleModel(hw, ridge, wHw, wRidge, start, y);
    }

    private static double Mse(double[] actual, double[] pred)
    {
        double s = 0;
        for (int i = 0; i < actual.Length; i++) { double d = actual[i] - pred[i]; s += d * d; }
        return s / actual.Length;
    }

    public static EnsembleModel Fit(double[] y, DateOnly start, int validationDays = 28)
    {
        if (y.Length < 60) throw new ArgumentException("Need at least 60 days of history", nameof(y));
        int v = Math.Min(validationDays, y.Length / 4);
        var train = y[..(y.Length - v)];
        var actual = y[(y.Length - v)..];
        var probe = FitComponents(train, start, 0.5, 0.5);
        double mh = Mse(actual, probe.PredictHoltWinters(v)) + 1e-9;
        double mr = Mse(actual, probe.PredictRidge(v)) + 1e-9;
        double wh = (1 / mh) / (1 / mh + 1 / mr);
        return FitComponents(y, start, wh, 1 - wh);
    }

    public static ForecastResult Forecast(double[] y, DateOnly start, int horizon, double confidence = 0.90, int origins = 20)
    {
        var model = Fit(y, start);
        var point = model.Predict(horizon);
        var dailyErrors = new List<double>[horizon];
        for (int h = 0; h < horizon; h++) dailyErrors[h] = [];
        var cumErrors = new List<double[]>();
        for (int k = 0; k < origins; k++)
        {
            int o = y.Length - horizon - 7 * k;
            if (o < 150) break;
            var m = Fit(y[..o], start);
            var p = m.Predict(horizon);
            var ce = new double[horizon];
            double cum = 0;
            for (int h = 0; h < horizon; h++)
            {
                double e = y[o + h] - p[h];
                dailyErrors[h].Add(e);
                cum += e;
                ce[h] = cum;
            }
            cumErrors.Add(ce);
        }

        var dates = new DateOnly[horizon];
        var lower = new double[horizon];
        var upper = new double[horizon];
        var cumPoint = new double[horizon];
        var cumLower = new double[horizon];
        var cumUpper = new double[horizon];
        double run = 0;
        double fallbackSd = Stats.StdDev(y);
        double tail = (1 - confidence) / 2;
        for (int h = 0; h < horizon; h++)
        {
            dates[h] = start.AddDays(y.Length + h);
            run += point[h];
            cumPoint[h] = run;
            double half, lo, hi;
            if (cumErrors.Count >= 5)
            {
                var pool = new List<double>();
                for (int j = Math.Max(0, h - 3); j <= Math.Min(horizon - 1, h + 3); j++)
                    pool.AddRange(dailyErrors[j].Select(Math.Abs));
                double level = Math.Min(1.0, Math.Ceiling((pool.Count + 1) * confidence) / pool.Count);
                half = Stats.Quantile(pool, level);
                var cpool = new List<double>();
                for (int j = Math.Max(0, h - 2); j <= Math.Min(horizon - 1, h + 2); j++)
                    cpool.AddRange(cumErrors.Select(e => e[j]));
                lo = Stats.Quantile(cpool, tail);
                hi = Stats.Quantile(cpool, 1 - tail);
            }
            else
            {
                double z = Stats.NormalInvCdf(1 - tail);
                half = z * fallbackSd;
                lo = -z * fallbackSd * Math.Sqrt(h + 1);
                hi = z * fallbackSd * Math.Sqrt(h + 1);
            }
            lower[h] = point[h] - half;
            upper[h] = point[h] + half;
            cumLower[h] = cumPoint[h] + lo;
            cumUpper[h] = cumPoint[h] + hi;
        }
        return new ForecastResult(dates, point, lower, upper, cumPoint, cumLower, cumUpper,
            model.WeightHoltWinters, model.WeightRidge, cumErrors.Count, confidence, cumErrors.ToArray());
    }
}
